//
// Copyright (C) 2015 crosire & contributors
// License: https://github.com/crosire/ScriptHookRDR2dotnet#license
//

#pragma managed(push, off)

#include <Windows.h>
#include <atomic>

// StreamEmber: CLR thread model (ScriptHookVDotNet 3.7 approach, scripthookvdotnet#976). Managed code runs on a
// dedicated thread instead of ScriptHookRDR2's script fiber: the CLR caches stack limits per thread, and running it
// on a fiber leads to (false) stack overflows and random runtime crashes when exceptions are dispatched. Natives are
// then invoked with the game main thread's TLS context while that thread waits in ScriptMain.
// Runtime.ini: ThreadingModel=Fiber (default, ScriptHookRDR2DotNet behaviour) | Thread (experimental: crashed the
// game shortly after a script started calling natives in the first in-game test, so it is opt-in only).
static bool sUseClrThread = false;
static LPVOID sTlsContextAddrOfGameMainThread = nullptr;
static DWORD sGameMainThreadId = 0;
static std::atomic_bool sGameMainThreadVarsInitialized(false);
static std::atomic_bool sScriptDomainRequestedToReload(false);

static void SetTlsContext(LPVOID context)
{
	__writegsqword(0x58, reinterpret_cast<DWORD64>(context));
}
static LPVOID GetTlsContext()
{
	return reinterpret_cast<LPVOID>(__readgsqword(0x58));
}

// Accessors for managed code (<atomic> must not be used from /clr code)
static bool IsGameMainThreadKnown()
{
	return sUseClrThread && sGameMainThreadVarsInitialized.load(std::memory_order_acquire);
}
static LPVOID GetGameMainThreadTlsContext()
{
	return sTlsContextAddrOfGameMainThread;
}
static DWORD GetGameMainThreadId()
{
	return sGameMainThreadId;
}

#pragma managed(pop)

// Has to be a managed variable since C++ exceptions will be ruined if this is unmanaged one
bool sGameReloaded = false;

// Import C# code base
#using "StreamEmber.Runtime.RDR2.netmodule" // = $(TargetName).netmodule (StreamEmberLayout.RuntimeAssemblyName)

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Reflection;
namespace WinForms = System::Windows::Forms;

[assembly:AssemblyTitle("StreamEmber Runtime (RDR2)")] ;
[assembly:AssemblyDescription("StreamEmber .NET script runtime for Red Dead Redemption 2. Based on ScriptHookRDR2DotNet-V2.")] ;
[assembly:AssemblyCompany("Stream Ember Platform")] ;
[assembly:AssemblyProduct("StreamEmber Runtime")] ;
[assembly:AssemblyCopyright("Copyright (c) 2015 crosire, (c) 2019 Salty, (c) Stream Ember Platform")] ;
// StreamEmber: versions come from Directory.Build.props (SE_VERSION) through the vcxproj
[assembly:AssemblyVersion(SE_FILE_VERSION)] ;
[assembly:AssemblyFileVersion(SE_FILE_VERSION)] ;
[assembly:AssemblyInformationalVersion(SE_VERSION)] ;
// Sign with a strong name to distinguish from older versions and cause .NET framework runtime to bind the correct assemblies
// There is no version check performed for assemblies without strong names (https://docs.microsoft.com/en-us/dotnet/framework/deployment/how-the-runtime-locates-assemblies)
[assembly:AssemblyKeyFileAttribute("PublicKeyToken.snk")] ;


public ref class ScriptHookRDRDotNet // This is not a static class, so that console scripts can inherit from it for ConsoleInput class
{
public:
	[RDR2DN::ConsoleCommand("Print the default help")]
	static void Help()
	{
		console->PrintInfo("~c~--- Help ---");
		console->PrintInfo("The console accepts ~h~C# expressions~h~ as input and has full access to the scripting API. To print the result of an expression, simply add \"return\" in front of it.");
		console->PrintInfo("You can use \"P\" as a shortcut for the player character and \"V\" for the current vehicle (without the quotes).");
		console->PrintInfo("Example: \"return P.IsAlive\" will print a boolean value to the console indicating whether the player is currently alive.");
		console->PrintInfo("~c~--- Commands ---");
		console->PrintHelpText();
	}

	[RDR2DN::ConsoleCommand("Print the help for a specific command")]
	static void Help(String^ command)
	{
		console->PrintHelpText(command);
	}

	[RDR2DN::ConsoleCommand("Clear the console history and pages")]
	static void Clear()
	{
		console->Clear();
	}

	[RDR2DN::ConsoleCommand("Reload all scripts from the scripts directory")]
	static void Reload()
	{
		console->PrintInfo("~y~Reloading ...");

		// Force a reload on next tick
		sGameReloaded = true;
	}

	[RDR2DN::ConsoleCommand("Load scripts from a file")]
	static void Start(String^ filename)
	{
		if (!IO::Path::IsPathRooted(filename))
			filename = IO::Path::Combine(domain->ScriptPath, filename);
		if (!IO::Path::HasExtension(filename))
			filename += ".dll";

		String^ ext = IO::Path::GetExtension(filename)->ToLower();
		if (!IO::File::Exists(filename) || (ext != ".cs" && ext != ".vb" && ext != ".dll")) {
			console->PrintError(IO::Path::GetFileName(filename) + " is not a script file!");
			return;
		}

		domain->StartScripts(filename);
	}

	[RDR2DN::ConsoleCommand("Abort all scripts from a file")]
	static void Abort(String^ filename)
	{
		if (!IO::Path::IsPathRooted(filename))
			filename = IO::Path::Combine(domain->ScriptPath, filename);
		if (!IO::Path::HasExtension(filename))
			filename += ".dll";

		String^ ext = IO::Path::GetExtension(filename)->ToLower();
		if (!IO::File::Exists(filename) || (ext != ".cs" && ext != ".vb" && ext != ".dll")) {
			console->PrintError(IO::Path::GetFileName(filename) + " is not a script file!");
			return;
		}

		domain->AbortScripts(filename);
	}

	[RDR2DN::ConsoleCommand("Abort all scripts currently running")]
	static void AbortAll()
	{
		domain->Abort();

		console->PrintInfo("Stopped all running scripts. Use \"Start(filename)\" to start them again.");
	}

	[RDR2DN::ConsoleCommand("List all loaded scripts")]
	static void ListScripts()
	{
		console->PrintInfo("~c~--- Loaded Scripts ---");
		for each (auto script in domain->RunningScripts)
			console->PrintInfo(IO::Path::GetFileName(script->FileName) + " ~h~" + script->Name + (script->IsRunning ? (script->IsPaused ? " ~o~[paused]" : " ~g~[running]") : " ~r~[aborted]"));
	}

internal:
	static RDR2DN::Console^ console = nullptr;
	static RDR2DN::ScriptDomain^ domain = RDR2DN::ScriptDomain::CurrentDomain;
	static WinForms::Keys reloadKey = WinForms::Keys::None;
	static WinForms::Keys consoleKey = WinForms::Keys::F8;
	static unsigned int scriptTimeoutThreshold = 5000;

	static void SetConsole()
	{
		console = (RDR2DN::Console^)AppDomain::CurrentDomain->GetData("Console");
	}

	// Keyboard messages arrive on the game's window thread. Nothing that touches the console, the script domain or
	// natives may run there, so they are queued and replayed on the script fiber in ManagedTick.
	// Packed as: low 32 bits = Keys (with modifiers), bit 32 = key down.
	static System::Collections::Concurrent::ConcurrentQueue<UInt64>^ keyQueue =
		gcnew System::Collections::Concurrent::ConcurrentQueue<UInt64>();

	// Errors at the unmanaged boundary are logged, never rethrown into the game (throttled to keep the log small).
	static int boundaryErrorCount = 0;
	static void LogBoundaryError(String^ where, Exception^ ex)
	{
		int n = ++boundaryErrorCount;
		if (n <= 20 || (n % 1000) == 0)
		{
			try
			{
				RDR2DN::Log::Message(RDR2DN::Log::Level::Error, where, " failed (#", n.ToString(), "): ", ex->ToString());
			}
			catch (...)
			{
			}
		}
	}
};

static void ForceCLRInit()
{
	// Just a function that doesn't do anything, except for being compiled to MSIL
}

static void ScriptHookRDRDotNet_ManagedInit()
{
	RDR2DN::Console^% console = ScriptHookRDRDotNet::console;
	RDR2DN::ScriptDomain^% domain = ScriptHookRDRDotNet::domain;
	List<String^>^ stashedConsoleCommandHistory = gcnew List<String^>();

	// Unload previous domain (this unloads all script assemblies too)
	if (domain != nullptr)
	{
		// Stash the command history if console is loaded 
		if (console != nullptr)
		{
			try
			{
				stashedConsoleCommandHistory = console->CommandHistory;
			}
			catch (Exception^)
			{
			}
		}

		// The console lives inside the old domain; a stale proxy would throw AppDomainUnloadedException every tick
		console = nullptr;

		RDR2DN::ScriptDomain^ oldDomain = domain;
		domain = nullptr;
		try
		{
			RDR2DN::ScriptDomain::Unload(oldDomain);
		}
		catch (Exception^ ex)
		{
			ScriptHookRDRDotNet::LogBoundaryError("ScriptDomain::Unload", ex);
		}
	}

	// Key events queued for the old domain are meaningless now
	UInt64 dropped;
	while (ScriptHookRDRDotNet::keyQueue->TryDequeue(dropped))
	{
	}


	// StreamEmber: everything lives under <game>\StreamEmber (see StreamEmberLayout.cs)
	RDR2DN::StreamEmberLayout::EnsureWritableDirectories();

	// Clear log from previous runs
	RDR2DN::Log::Clear();

	// Load configuration
	String^ scriptPath = RDR2DN::StreamEmberLayout::ScriptsDirectory;

	try
	{
		array<String^>^ config = IO::File::ReadAllLines(RDR2DN::StreamEmberLayout::ConfigFile);

		for each (String ^ line in config)
		{
			// Perform some very basic key/value parsing
			line = line->Trim();
			if (line->StartsWith("//") || line->StartsWith(";") || line->StartsWith("#"))
				continue;
			array<String^>^ data = line->Split('=');
			if (data->Length != 2)
				continue;

			// May fail to parse without trimming whitespaces
			String^ keyStr = data[0]->Trim();
			String^ valueStr = data[1]->Trim();

			if (String::Equals(keyStr, "ReloadKey", StringComparison::OrdinalIgnoreCase))
				Enum::TryParse(valueStr, true, ScriptHookRDRDotNet::reloadKey);
			else if (String::Equals(keyStr, "ConsoleKey", StringComparison::OrdinalIgnoreCase))
				Enum::TryParse(valueStr, true, ScriptHookRDRDotNet::consoleKey);
			else if (String::Equals(keyStr, "ScriptTimeoutThreshold", StringComparison::OrdinalIgnoreCase))
			{
				unsigned int outVal;
				if (UInt32::TryParse(valueStr, outVal))
				{
					ScriptHookRDRDotNet::scriptTimeoutThreshold = outVal;
				}
			}
			else if (String::Equals(keyStr, "ScriptsLocation", StringComparison::OrdinalIgnoreCase))
				scriptPath = RDR2DN::StreamEmberLayout::ResolveGamePath(valueStr->Trim('"'));
		}
	}
	catch (Exception^ ex)
	{
		RDR2DN::Log::Message(RDR2DN::Log::Level::Error, "Failed to load config: ", ex->ToString());
	}

	// Create a separate script domain
	domain = RDR2DN::ScriptDomain::Load(RDR2DN::StreamEmberLayout::RuntimeDirectory, scriptPath);
	if (domain == nullptr)
	{
		RDR2DN::Log::Message(RDR2DN::Log::Level::Error, "ScriptDomain::Load() returned null in ", scriptPath);
		return;
	}

	// CLR thread model: natives run on script threads with the game main thread's TLS context
	if (IsGameMainThreadKnown())
	{
		domain->InitTlsContextSwitch(
			IntPtr(reinterpret_cast<void*>(&GetTlsContext)),
			IntPtr(reinterpret_cast<void*>(&SetTlsContext)),
			IntPtr(GetGameMainThreadTlsContext()),
			static_cast<UInt32>(GetGameMainThreadId()));
	}
	RDR2DN::Log::Message(RDR2DN::Log::Level::Info, "Threading model: ",
		domain->IsTlsContextSwitchEnabled ? "dedicated CLR thread" : "ScriptHookRDR2 fiber");

	domain->ScriptTimeoutThreshold = ScriptHookRDRDotNet::scriptTimeoutThreshold;

	// Console Stuff
	try
	{
		// Instantiate console inside script domain, so that it can access the scripting API
		console = (RDR2DN::Console^)domain->AppDomain->CreateInstanceFromAndUnwrap(
			RDR2DN::Console::typeid->Assembly->Location, RDR2DN::Console::typeid->FullName);

		// Restore the console command history (set a empty history for the first time)
		console->CommandHistory = stashedConsoleCommandHistory;

		// Print welcome message
		console->PrintInfo(String::Concat("~c~--- StreamEmber Runtime (RDR2) ", RDR2DN::StreamEmberLayout::ProductVersion, " ---"));
		console->PrintInfo("~c~--- Type \"Help()\" to print an overview of available commands ---");

		// Update console pointer in script domain
		domain->AppDomain->SetData("Console", console);
		domain->AppDomain->DoCallBack(gcnew CrossAppDomainDelegate(&ScriptHookRDRDotNet::SetConsole));

		// Add default console commands
		console->RegisterCommands(ScriptHookRDRDotNet::typeid);
	}
	catch (Exception^ ex)
	{
		RDR2DN::Log::Message(RDR2DN::Log::Level::Error, "Failed to create console: ", ex->ToString());
	}

	// Start scripts in the newly created domain
	domain->Start();
}

static void ScriptHookRDRDotNet_DispatchKey(WinForms::Keys keys, bool keydown);

static void ScriptHookRDRDotNet_SafeInit()
{
	try
	{
		ScriptHookRDRDotNet_ManagedInit();
	}
	catch (Exception^ ex)
	{
		ScriptHookRDRDotNet::LogBoundaryError("ManagedInit", ex);
	}
}

static void ScriptHookRDRDotNet_ManagedTick()
{
	// Replay keyboard messages from the window thread on this (script) fiber
	UInt64 packed;
	int budget = 256;
	while (budget-- > 0 && ScriptHookRDRDotNet::keyQueue->TryDequeue(packed))
	{
		try
		{
			ScriptHookRDRDotNet_DispatchKey(safe_cast<WinForms::Keys>(static_cast<int>(packed & 0xFFFFFFFFull)), (packed >> 32) != 0);
		}
		catch (Exception^ ex)
		{
			ScriptHookRDRDotNet::LogBoundaryError("KeyEvent", ex);
		}
	}

	RDR2DN::Console^ console = ScriptHookRDRDotNet::console;
	if (console != nullptr)
	{
		try
		{
			console->DoTick();
		}
		catch (AppDomainUnloadedException^)
		{
			ScriptHookRDRDotNet::console = nullptr;
		}
		catch (Exception^ ex)
		{
			ScriptHookRDRDotNet::LogBoundaryError("Console::DoTick", ex);
		}
	}

	RDR2DN::ScriptDomain^ scriptdomain = ScriptHookRDRDotNet::domain;
	if (scriptdomain != nullptr)
	{
		try
		{
			scriptdomain->DoTick();
		}
		catch (Exception^ ex)
		{
			ScriptHookRDRDotNet::LogBoundaryError("ScriptDomain::DoTick", ex);
		}
	}
}

static void ScriptHookRDRDotNet_SafeTick()
{
	try
	{
		ScriptHookRDRDotNet_ManagedTick();
	}
	catch (Exception^ ex)
	{
		ScriptHookRDRDotNet::LogBoundaryError("ManagedTick", ex);
	}
}

static void ScriptHookRDRDotNet_ManagedKeyboardMessage(unsigned long keycode, bool keydown, bool ctrl, bool shift, bool alt)
{
	// Runs on the window thread: only validate and enqueue
	try
	{
		// Filter out invalid key codes
		if (keycode <= 0 || keycode >= 256)
			return;

		UInt32 keys = static_cast<UInt32>(keycode);
		if (ctrl)  keys |= static_cast<UInt32>(WinForms::Keys::Control);
		if (shift) keys |= static_cast<UInt32>(WinForms::Keys::Shift);
		if (alt)   keys |= static_cast<UInt32>(WinForms::Keys::Alt);

		// Bound the queue in case the script fiber is not ticking (loading screens, pause)
		if (ScriptHookRDRDotNet::keyQueue->Count < 1024)
			ScriptHookRDRDotNet::keyQueue->Enqueue(static_cast<UInt64>(keys) | (keydown ? (1ull << 32) : 0ull));
	}
	catch (Exception^)
	{
		// Never throw into the game's window procedure
	}
}

static void ScriptHookRDRDotNet_DispatchKey(WinForms::Keys keys, bool keydown)
{
	RDR2DN::Console^ console = ScriptHookRDRDotNet::console;
	if (console != nullptr)
	{
		if (keydown && keys == ScriptHookRDRDotNet::reloadKey)
		{
			// Force a reload
			ScriptHookRDRDotNet::Reload();
			return;
		}
		if (keydown && keys == ScriptHookRDRDotNet::consoleKey)
		{
			// Toggle open state
			console->IsOpen = !console->IsOpen;
			return;
		}

		// Send key events to console
		console->DoKeyEvent(keys, keydown);

		// Do not send keyboard events to other running scripts when console is open
		if (console->IsOpen)
			return;
	}

	RDR2DN::ScriptDomain ^scriptDomain = ScriptHookRDRDotNet::domain;
	if (scriptDomain != nullptr)
	{
		// Send key events to all scripts
		scriptDomain->DoKeyEvent(keys, keydown);
	}
}

#pragma unmanaged

#include <Main.h>
#include <string.h>
#include <wchar.h>

PVOID sGameFiber = nullptr;

// --- CLR thread model -------------------------------------------------------------------------------------------
static std::atomic<HANDLE> hClrThread{ nullptr };
static std::atomic<HANDLE> hClrWaitEvent{ nullptr };
static std::atomic<HANDLE> hClrContinueEvent{ nullptr };
static std::atomic_bool sClrThreadRequestedToExit(false);
static PVOID sOldGameFiber = nullptr;

static DWORD WINAPI ClrThreadProc(LPVOID)
{
	// Load the CLR on this thread before anything else (matches the CLR DLLs' TLS slots with the game thread's)
	ForceCLRInit();

	// DllMain runs before ScriptHookRDR2 starts script fibers: wait until ScriptMain signals the first tick
	WaitForSingleObject(hClrContinueEvent.load(std::memory_order_relaxed), INFINITE);

	while (!sClrThreadRequestedToExit.load(std::memory_order_relaxed))
	{
		sGameReloaded = false;
		sScriptDomainRequestedToReload.store(false, std::memory_order_release);
		ScriptHookRDRDotNet_SafeInit();

		// One managed tick per game tick; the game main thread waits in ScriptMain meanwhile
		while (!sGameReloaded
			&& !sScriptDomainRequestedToReload.load(std::memory_order_acquire)
			&& !sClrThreadRequestedToExit.load(std::memory_order_relaxed))
		{
			ScriptHookRDRDotNet_SafeTick();
			SetEvent(hClrWaitEvent.load(std::memory_order_relaxed));
			WaitForSingleObject(hClrContinueEvent.load(std::memory_order_relaxed), INFINITE);
		}
	}
	return 0;
}

static void ScriptMainClrThread()
{
	// ScriptHookRDR2 already turned the current thread into a fiber
	const PVOID initialGameFiber = GetCurrentFiber();
	if (sOldGameFiber != nullptr)
	{
		// ScriptHookRDR2 restarted its scripts (new game session, checkpoint/mission retry): reload the domain
		sScriptDomainRequestedToReload.store(true, std::memory_order_release);
	}
	sOldGameFiber = initialGameFiber;

	while (!sClrThreadRequestedToExit.load(std::memory_order_acquire))
	{
		// A new fiber means ScriptHookRDR2 is disposing this one: leave without touching anything
		if (GetCurrentFiber() != initialGameFiber)
		{
			break;
		}

		SetEvent(hClrContinueEvent.load(std::memory_order_relaxed));
		// Blocks the game main thread while managed code runs, so natives can borrow its TLS context safely
		WaitForSingleObject(hClrWaitEvent.load(std::memory_order_relaxed), INFINITE);
		scriptWait(0);
	}
}

// --- Fiber model (ScriptHookRDR2DotNet) -------------------------------------------------------------------------
static void ScriptMainFiber()
{
	// ScriptHookRDR2 already turned the current thread into a fiber, so we can safely retrieve it.
	sGameFiber = GetCurrentFiber();

	while (true)
	{
		sGameReloaded = false;

		ScriptHookRDRDotNet_SafeInit();

		while (!sGameReloaded)
		{
			// ScriptHookRDR2 creates a new fiber only right after a "Started thread" message is written to the log
			const PVOID currentFiber = GetCurrentFiber();
			if (currentFiber != sGameFiber)
			{
				sGameFiber = currentFiber;
				sGameReloaded = true;
				break;
			}

			ScriptHookRDRDotNet_SafeTick();
			scriptWait(0);
		}
	}
}

static void ScriptMain()
{
	// The game main thread's TLS context and id: natives on other threads borrow this context
	if (!sGameMainThreadVarsInitialized.load(std::memory_order_acquire))
	{
		sTlsContextAddrOfGameMainThread = GetTlsContext();
		sGameMainThreadId = GetCurrentThreadId();
		sGameMainThreadVarsInitialized.store(true, std::memory_order_release);
	}

	if (sUseClrThread)
		ScriptMainClrThread();
	else
		ScriptMainFiber();
}

// Reads ThreadingModel from <game>\StreamEmber\Config\Runtime.ini (kernel32 only: called from DllMain).
static bool ReadUseClrThread(HMODULE hModule)
{
	wchar_t path[MAX_PATH];
	const DWORD len = GetModuleFileNameW(hModule, path, MAX_PATH);
	if (len == 0 || len >= MAX_PATH)
		return false;
	wchar_t* slash = wcsrchr(path, L'\\');
	if (slash == nullptr)
		return false;
	*slash = L'\0';
	if (wcscat_s(path, MAX_PATH, L"\\StreamEmber\\Config\\Runtime.ini") != 0)
		return false;

	const HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
	if (file == INVALID_HANDLE_VALUE)
		return false;
	char text[16384];
	DWORD read = 0;
	const BOOL ok = ReadFile(file, text, sizeof(text) - 1, &read, NULL);
	CloseHandle(file);
	if (!ok)
		return false;
	text[read] = '\0';

	bool useThread = false;
	for (char* line = text; line != nullptr && *line != '\0'; )
	{
		char* next = strchr(line, '\n');
		if (next != nullptr)
			*next++ = '\0';
		while (*line == ' ' || *line == '\t' || *line == '\r')
			++line;
		if (_strnicmp(line, "ThreadingModel", 14) == 0)
		{
			char* value = strchr(line, '=');
			if (value != nullptr)
			{
				++value;
				while (*value == ' ' || *value == '\t' || *value == '"')
					++value;
				useThread = _strnicmp(value, "Thread", 6) == 0;
			}
		}
		line = next;
	}
	return useThread;
}

static void ScriptKeyboardMessage(DWORD key, WORD repeats, BYTE scanCode, BOOL isExtended, BOOL isWithAlt, BOOL wasDownBefore, BOOL isUpNow)
{
	ScriptHookRDRDotNet_ManagedKeyboardMessage(
		key,
		!isUpNow,
		(GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
		(GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
		isWithAlt != FALSE);
}

BOOL WINAPI DllMain(HMODULE hModule, DWORD fdwReason, LPVOID lpvReserved)
{
	switch (fdwReason)
	{
	case DLL_PROCESS_ATTACH:
		// Avoid unnecessary DLL_THREAD_ATTACH and DLL_THREAD_DETACH notifications
		DisableThreadLibraryCalls(hModule);
		// Call a managed function to force the CLR to initialize immediately
		// This is technically a very bad idea (https://learn.microsoft.com/cpp/dotnet/initialization-of-mixed-assemblies), but fixes a crash that would otherwise occur when the CLR is initialized later on
		if (!GetModuleHandle(TEXT("clr.dll")))
			ForceCLRInit();
		sUseClrThread = ReadUseClrThread(hModule);
		if (sUseClrThread)
		{
			hClrContinueEvent.store(CreateEvent(NULL, FALSE, FALSE, NULL), std::memory_order_relaxed);
			hClrWaitEvent.store(CreateEvent(NULL, FALSE, FALSE, NULL), std::memory_order_relaxed);
			hClrThread.store(CreateThread(NULL, 0, ClrThreadProc, NULL, 0, NULL), std::memory_order_release);
			if (hClrThread.load(std::memory_order_relaxed) == nullptr)
				sUseClrThread = false;  // fall back to the fiber model
		}
		// Register ScriptHookRDRDotNet native script
		scriptRegister(hModule, ScriptMain);
		// Register handler for keyboard messages
		keyboardHandlerRegister(ScriptKeyboardMessage);
		break;
	case DLL_PROCESS_DETACH:
		if (hClrThread.load(std::memory_order_relaxed) != nullptr)
		{
			// Let the CLR thread and the script fiber leave their waits (the process is exiting)
			sClrThreadRequestedToExit.store(true, std::memory_order_relaxed);
			SetEvent(hClrContinueEvent.load(std::memory_order_relaxed));
			SetEvent(hClrWaitEvent.load(std::memory_order_relaxed));
			CloseHandle(hClrContinueEvent.load(std::memory_order_relaxed));
			CloseHandle(hClrWaitEvent.load(std::memory_order_relaxed));
			CloseHandle(hClrThread.load(std::memory_order_relaxed));
		}
		// Unregister ScriptHookRDRDotNet native script
		scriptUnregister(hModule);
		// Unregister handler for keyboard messages
		keyboardHandlerUnregister(ScriptKeyboardMessage);
		break;
	}

	return TRUE;
}


