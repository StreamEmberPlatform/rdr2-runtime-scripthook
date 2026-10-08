# StreamEmber Runtime (RDR2)

Red Dead Redemption 2 için StreamEmber'in .NET script çalışma ortamı. Oyunun içinde .NET Framework 4.8'i başlatır,
`StreamEmber\Scripts\` klasöründeki scriptleri yükler ve onlara `StreamEmber.Scripting.RDR2` API'sini verir.
[ScriptHookRDR2DotNet-V2](https://github.com/Halen84/ScriptHookRDR2DotNet-V2) (zlib; SHVDN'den türetilmiş) üzerine
kuruludur; kendi sürüm numarası, adları ve klasör düzeni olan bağımsız bir dağıtımdır.

```text
RDR2.exe
 └─ ScriptHookRDR2.dll           Alexander Blade (dev-c.com) — native çağrılar, script fiber'ları (ayrıca kurulur)
     └─ StreamEmber.Runtime.RDR2.asi            bu repo: .NET çalışma ortamı
         └─ StreamEmber.Scripting.RDR2.dll      bu repo: scriptlerin API'si (namespace RDR2)
             └─ StreamEmber\Scripts\*.dll       scriptler (ör. StreamEmber Trainer: rdr2-trainer-scripthook)
```

## Oyun klasöründeki düzen

| Dosya | Görev |
|---|---|
| `StreamEmber.Runtime.RDR2.asi` | Çalışma ortamı. ASI yükleyici (`dinput8.dll`) oyun kökünden yükler |
| `StreamEmber\Runtime\StreamEmber.Scripting.RDR2.dll` | Script API'si |
| `StreamEmber\Config\Runtime.ini` | Ayarlar (konsol tuşu F4, script zaman aşımı, scripts klasörü). Güncellemede korunur |
| `StreamEmber\Scripts\` | Scriptler |
| `StreamEmber\Logs\Runtime.log` | Log |
| `StreamEmber\Manifests\StreamEmber.Runtime.RDR2.json` | Paket manifest'i: sürüm, commit, dosyalar ve SHA-256 değerleri |
| `StreamEmber\Licenses\StreamEmber.Runtime.RDR2\` | Lisans |

Gereken: `ScriptHookRDR2.dll` ve `dinput8.dll` ([dev-c.com](http://www.dev-c.com/rdr2/scripthookrdr2/)).
ScriptHookRDR2DotNet ile birlikte kullanılmaz: kurulum `ScriptHookRDRDotNet.asi`'yi `.disabled` yapar.

> Topluluğun ScriptHookRDR2DotNet scriptleri (`ScriptHookRDRNetAPI.dll`'e göre derlenmiş) bu çalışma ortamında
> yüklenmez; scriptler `StreamEmber.Scripting.RDR2.dll`'e göre derlenir (`using RDR2;`).

## Sürümler ve yayın

- Sürüm: `VERSION` dosyası `major.minor`, patch = o dosyanın son değiştiği commit'ten bu yana commit sayısı.
  `main`'e her push yeni bir sürümdür: `v1.0.0`, `v1.0.1`, … Minör/majör artırmak için `VERSION`'ı değiştirip pushla.
- GitHub Actions (`.github/workflows/build.yml`): her push ve PR'da derleme; `main`'de ayrıca etiket ve GitHub Release
  (`StreamEmber.Runtime.RDR2-<sürüm>.zip` + `.sha256`). Zip'in kökü = oyun klasörü.
- Dış bağımlılık yok: ScriptHookRDR2 SDK'sının kullanılan iki dosyası (`sdk/inc/main.h`, `sdk/lib/ScriptHookRDR2.lib`)
  depodadır (`sdk/README.md`). `ScriptHookRDR2.dll`'in kendisi dağıtılmaz; oyuncu dev-c.com'dan kurar.
- Yerel derlemeler `-dev` ekiyle damgalanır (`1.0.5-dev`).
- DLL'lerde: dosya ve ürün sürümü = StreamEmber sürümü; API derlemesinin `AssemblyVersion`'ı API seviyesidir (`2.2.0.0`).

## Derleme

Visual Studio 2022+ ("Desktop development with C++" + C++/CLI desteği), .NET Framework 4.8 targeting pack.

```powershell
.\build.ps1                                                            # derle + dist\RDR2\ + artifacts\*.zip
.\build.ps1 -Deploy -GamePath "D:\SteamLibrary\steamapps\common\Red Dead Redemption 2"   # + oyuna kur (ya da RDR2_GAME_PATH)
```

## Upstream ile ilişki

| | |
|---|---|
| Upstream | https://github.com/Halen84/ScriptHookRDR2DotNet-V2 (`master`, 2023'ten beri durgun) |
| Taban | `03f2264` |
| Remote | `upstream` → Halen84 reposu, `origin` → StreamEmberPlatform/rdr2-runtime-scripthook |

İsim ve yollar yalnız `source/core/StreamEmberLayout.cs` içindedir; bizim değişiklikler `StreamEmber:` yorumlarıyla işaretli.

### Upstream'den farklarımız

| Değişiklik | Neden |
|---|---|
| Adlar ve klasör düzeni (`StreamEmberLayout.cs`), `.pdb`/`.xml` yok, sürüm kaynağı (`.rc`) | StreamEmber dağıtımı |
| **Ayrı CLR thread'i** (ScriptHookVDotNet 3.7 modeli), **deneysel, varsayılan kapalı**: `Runtime.ini` → `ThreadingModel=Thread`. Managed kod kendi thread'inde çalışır, native'ler oyun thread'inin TLS bağlamıyla doğrudan çağrılır. İlk oyun testinde trainer native çağırmaya başlayınca oyun ~30 sn içinde kapandı; sebebi bulunana kadar varsayılan `Fiber` | Hedef: fiber üzerindeki CLR çökmeleri (scripthookvdotnet#976), checkpoint tekrarında kopma (upstream #17), native başına thread devri |
| Scriptler `Runtime.ini` → `ScriptsLocation` klasöründen yüklenir (upstream her zaman `<oyun>\scripts`'e bakıyordu) | `StreamEmber\Scripts` hiç yüklenmiyordu |
| `World.GetAllPeds/Vehicles/Objects` ScriptHookRDR2 havuz fonksiyonlarını native'lerin çalıştığı yerde çağırır | Script thread'inden çağrılınca oyunun thread durumu bozuluyordu: rastgele erişim ihlalleri, bir süre sonra hep boş sonuç (upstream #2; "uzun oynayınca/çok NPC ölünce kimse bulunamıyor") |
| `World.GetNearbyPeds/Vehicles/Props` (oyunun kendi uzamsal sorgusu, itemset) | Her kare "etrafımdaki varlıklar" için tüm havuzu dolaşmadan; itemset her durumda serbest bırakılır |
| Derleme düzeltmesi (`Global.cs` FVector3, `Vehicle.cs` `Exists`) | Upstream HEAD derlenmiyordu |
| `ScriptStruct` (`RDR2.Native`): 8 baytlık slotlardan script struct'ı (int/float/hash/`const char*`) | `Any*` alan/dolduran native'ler (nearby tamponu, UI feed, shop item bileşenleri) elle marshal gerektiriyordu |
| `Ped.GetNearbyPeds/GetNearbyVehicles(max)` (GET_PED_NEARBY_* boyut önekli tampon) | Itemset açmadan, her kare güvenle "etrafımdaki varlıklar" |
| Yeni sarmalayıcılar: binek (`SetOnMount`, `DismountInstantly`, `IsMountSeatFree`, `Rider`, `AgitateHorse`), outfit preset, `SetDrunk`, `SetWalkStyle`, `FleeFrom`, savaş öznitelikleri, `GetShopItemComponents`/`ApplyShopItemComponents`; `Entity.HasGravity/SetDynamic/ApplyForceToCenterOfMass/Ignite`; `Player.DeadEye*`, `EagleEyeEnabled`, `ReportCrime`, `GroupId`; `World.SetWeather/WindSpeed/SnowLevel/ForceLightningFlash/SetClockTime/SetTimecycleModifier`, `World.CreatePed/CreateVehicle(Model)`; `Vehicle.BreakOffWheel/SeatCount/SetTrainSpeed`; `GameplayCamera.ForceFirstPersonThisFrame/Shake`; `Hud.HideThisFrame`; `Game.SetControlContext`; `Audio.PlaySoundFromEntity`; `UI.Feed.ShowToast` | ChaosModRDR incelemesi: native'leri vardı, üst seviye API yoktu |
| `Player.ChangeModelPersistent` / `RestoreStoryModel`: oyuncu ped'i ve model hash'i script global'leri (Global_35, Global_40.f_39, Global_1935630.f_2) güncellenir; yalnız beklenen değeri tutuyorlarsa yazılır | Yalnız `SET_PLAYER_MODEL` ile oyun modeli geri çeviriyor / eski ped handle'ını kullanıyordu; farklı oyun sürümünde rastgele global ezilmesin |
| `Global.Set/As` için `uint`, `long`, `ulong` | Hash yazmak `OverflowException`, `uint` okumak `InvalidCastException` atıyordu |
| Klavye mesajları pencere thread'inde kuyruğa alınır, script fiber'ında işlenir | Konsol ve domain pencere thread'inden çağrılıyordu |
| Init / Tick / klavye sınırında try/catch, kısıtlı log | Sızan managed exception oyunu kapatıyordu |
| Domain kaldırılmadan önce konsol bırakılır | Bayat konsol proxy'si her karede hata atıyordu |
| `ExecuteTask` yalnız çalışan script'in thread'inden | Başka thread'den native çağrısı semafor sırasını bozuyordu |
| `ScriptTimeoutThreshold` 100–60000 ms | `(int)uint` dönüşümü sonsuz bekleme olabiliyordu |
| `StringToCoTaskMemUTF8` → `AllocCoTaskMem` | Heap uyuşmazlığı |
| Script thread'leri arka plan thread'i, `Console.DoTick` hata yakalar, `Global` boş işaretçi kontrolü | Kapanış ve çökme güvenliği |


## Lisans

zlib ([LICENSE](LICENSE)). Upstream belgeleri: [docs/upstream](docs/upstream/README.md).
