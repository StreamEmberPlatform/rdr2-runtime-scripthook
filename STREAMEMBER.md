# RDR2ScriptHookRuntime

StreamEmber için kendi derlediğimiz **ScriptHookRDR2DotNet-V2** runtime'ı: `ScriptHookRDRDotNet.asi` +
`ScriptHookRDRNetAPI.dll`. RDR2 tarafındaki modlarımız ve OverlayRuntime'ın RDR2 örnekleri bunun üzerinde çalışır.
GTA V tarafındaki karşılığı `../GTAVScriptHookRuntime`. Bu klasör upstream'in bir fork'udur; upstream dosyalarını
(README.md, source/…) gerekmedikçe değiştirmeyin.

## Kaynak eşlemesi

| | |
|---|---|
| Upstream | https://github.com/Halen84/ScriptHookRDR2DotNet-V2 (`master`) |
| Taban commit | `03f2264` "Add Entity.MarkAsMissionEntity() and Entity.MarkAsNoLongerNeeded()" (2023-07-13) |
| Assembly sürümü | `1.5.5.4` (`source/core/DllMain.cpp`) |
| ScriptHookRDR2 SDK | `1.0.1207.73` (dev-c.com) |

Upstream 2023'ten beri güncellenmiyor ve son commit'i derlenmiyordu (aşağıda). Yeni bir upstream commit gelirse
`git fetch upstream` → `git merge upstream/master`.

## Git düzeni

- Remote `upstream` → Halen84 reposu. `origin` yok (kendi uzak repomuz açılınca eklenir).
- Çalışma dalı: `streamember/main`. Bizim değişikliklerimiz yalnız burada.

## Fork değişiklikleri

| Commit | Değişiklik | Neden |
|---|---|---|
| `fix(build)` | `Global.cs` Vector3 → `NativeMemory.FVector3`; `Vehicle.cs` `Ped.Exists(x)` → `x != null && x.Exists()` | Upstream HEAD 7 hata ile derlenmiyordu (c0cc073 statik `Exists`'i kaldırmış) |
| `fix(core)` | Klavye mesajları pencere thread'inde kuyruğa alınır, script fiber'ında (ManagedTick) işlenir | Konsol aç/kapa (`IsOpen` → native) ve domain çağrıları oyunun pencere thread'inden yapılıyordu |
| | Init / Tick / klavye sınırında try/catch, kısıtlı log (`LogBoundaryError`) | Sızan managed exception oyunu kapatıyordu |
| | Domain kaldırılmadan önce `console = nullptr`; eski domain'in tuş kuyruğu boşaltılır | Bayat konsol proxy'si her karede `AppDomainUnloadedException` atıyordu |
| | `ExecuteTask` yalnız o an çalışan script'in thread'inden kabul edilir, diğerlerine `InvalidOperationException` | `Task.Run`/timer'dan native çağrısı semafor sırasını bozuyor, `_executingScript` null ise NRE veriyordu |
| | `ScriptTimeoutThreshold` 100–60000 ms aralığına sıkıştırılır | `(int)uint` dönüşümü -1 = sonsuz bekleme olabiliyordu |
| | `StringToCoTaskMemUTF8` → `AllocCoTaskMem` | `AllocHGlobal` ile ayrılıp `FreeCoTaskMem` ile bırakılıyordu (heap uyuşmazlığı) |
| | Script thread'leri `IsBackground = true` | Oyun kapanırken süreci açık tutabiliyordu |
| | `Console.DoTick` her exception'ı yakalar | Hatalı derleme görevi (`Task.Result`) tick'ten dışarı sızıyordu |
| | `Global` / `GlobalCollection` boş global işaretçisinde exception atar | Bilinmeyen global indeksi 0 adresine yazıyordu |

### Bilinen, henüz yapılmamış iş

- **Managed kod ScriptHook fiber'ında çalışıyor** (SHVDN #976 ile aynı sorun). Fiber geçişlerinde CLR'ın thread
  durumu karışabilir. Asıl kalıcı çözüm SHVDN'in 3.7 yaklaşımı: ayrı bir CLR thread'i + native çağrıları için TLS
  takası. Büyük değişiklik; RDR2 overlay denemeleri sonrasına bırakıldı.
- CLR, `DllMain` içinde (loader lock altında) başlatılıyor. Upstream bunu bilerek yapıyor (daha geç başlatma
  çöküyordu); dokunulmadı.

## Klasörler

```text
source/core/              C++/CLI .asi (ScriptHookRDRDotNet.vcxproj) + core C# netmodule
source/scripting_v3/      API (ScriptHookRDRNetAPI.dll)
sdk/                      build.ps1'in SDK'dan kopyaladığı inc\ + lib\ (git dışı, sdk/.gitignore)
vendor/ScriptHookRDR2_SDK_1.0.1207.73/   tam SDK (Downloads'tan kopya) — git dışı, yeniden dağıtımı yasak
ScriptHookRDRDotNet.ini   varsayılan ayarlar (oyun klasöründe yoksa kopyalanır)
build.ps1                 StreamEmber derleme betiği
builds/                   build.ps1 çıktıları (git dışı)
```

## Derleme

Ön koşullar (Visual Studio 2022+): "Desktop development with C++" + **C++/CLI support (v143)**,
**.NET Framework 4.8 targeting pack**, Windows 10/11 SDK.

```powershell
.\build.ps1                                   # Release x64 → builds\1.5.5.4\
.\build.ps1 -GameDir "D:\Games\Red Dead Redemption 2"   # derle + oyuna kur
.\build.ps1 -SdkPath C:\path\ScriptHookRDR2_SDK_1.0.1207.73
```

SDK sırası: `-SdkPath` → `vendor\ScriptHookRDR2_SDK_*` → `%USERPROFILE%\Downloads\ScriptHookRDR2_SDK_*`.
`tools/install_sdk.ps1` (upstream) SDK'yı HTTP ile indirir; biz kullanmıyoruz.

## Oyuna kurulum

`RDR2.exe` klasörüne: Alexander Blade'in `ScriptHookRDR2.dll` + `dinput8.dll` (ASI loader), sonra
`ScriptHookRDRDotNet.asi` + `ScriptHookRDRNetAPI.dll` **birlikte**, `ScriptHookRDRDotNet.ini` ve `scripts\` klasörü.
Scriptler `scripts\` içine (.dll / .cs / .vb). Konsol: F8. Log: `ScriptHookRDRDotNet.log`.
