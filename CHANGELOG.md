# Değişiklik günlüğü

Sürümler `VERSION` (major.minor) + commit sayısı (patch) ile otomatik verilir; her `main` push'u bir sürümdür.
Burada yalnız kayda değer değişiklikler tutulur.

## Yayımlanmamış — 2026-10-09
- Deneysel Thread modeli güvenli Fiber yoluna düşürülür. Native ağırlıklı döngüler için tick bütçesi; Live kuyruğu 2048 ve tick başına 25 aksiyonla sınırlıdır.

## 1.2
- StreamEmber Live (`StreamEmber.Live`): canlı yayın modları için gömülü EventFabric/GCore bağlantısı, Falcon ayarları,
  presence ve Identity v2. Modlar `LiveScript`'ten türer; aksiyonlar `On(…)`, `[LiveAction]`, `ActionReceived` ile gelir.
  Okuma customer UUID ile, EventFabric'e yazma yalnız runtime token ile. Eski GTAVScriptHook core'u ve modları desteklenmez.
  `Runtime.ini`'ye `Live*` anahtarları eklendi (eksikse varsayılanlar). Ayrıntı: `docs/StreamEmber-Live.md`.

## 1.1
- Yeni API (ChaosModRDR incelemesinden): `ScriptStruct` (struct alan native'ler), `Ped.GetNearbyPeds/GetNearbyVehicles(max)`
  (itemset'siz), binek, outfit preset, sarhoşluk, yürüyüş stili, kaçma, savaş öznitelikleri, kıyafet kaydet/geri yükle,
  yerçekimi/fizik/ateş, Dead Eye/Eagle Eye, suç bildirme, hava/rüzgâr/kar/yıldırım/saat/timecycle, `Model` ile spawn,
  tekerlek koparma, tren hızı, birinci şahıs zorlama, kamera sarsıntısı, HUD gizleme, kontrol bağlamı, varlıktan ses,
  oyunun bildirim akışı (`RDR2.UI.Feed.ShowToast`).
- `Player.ChangeModelPersistent` / `RestoreStoryModel`: model değişince hikâye global'leri de güncellenir (yalnız doğrulanırsa).
- `Global.Set/As` artık `uint`, `long`, `ulong` destekliyor.
- `Screen.PlayEffect` efekti yüklenmeyi beklemeden oynatır (önce yalnız aynı karede yüklüyse oynuyor, sonra boşaltılıyordu).
- `ChangeModelPersistent` yeni ped'e outfit verir (yoksa görünmez kalıyordu); `RestoreStoryModel` Arthur/John'u ped'in
  kendi modelinden hatırlar (global'ler doğrulanamasa da John, John olarak döner) ve model yüklüyse beklemez.

## 1.0
- Deneysel CLR thread modeli (ScriptHookVDotNet 3.7): `Runtime.ini` → `ThreadingModel=Thread`. Varsayılan `Fiber`:
  ilk oyun testinde Thread modeli scriptler başladıktan kısa süre sonra oyunu kapattı.
- Scriptler `ScriptsLocation` klasöründen (`StreamEmber\Scripts`) yüklenir.
- `World.GetAllPeds/Vehicles/Objects` artık oyunun durumunu bozmuyor (uzun oyunda varlıkların "kaybolması" düzeldi);
  yeni `World.GetNearbyPeds/Vehicles/Props`.
- İlk StreamEmber dağıtımı: `StreamEmber.Runtime.RDR2.asi` + `StreamEmber.Scripting.RDR2.dll`, `StreamEmber\` klasör
  düzeni (Runtime, Scripts, Config, Logs, Manifests, Licenses), kendi sürüm numaraları.
- `.pdb` ve `.xml` dosyaları dağıtımdan çıkarıldı; ScriptHookRDR2 SDK'sının gereken dosyaları depoda (dış bağımlılık yok).
- Çökme sertleştirmeleri (bkz. README, "Upstream'den farklarımız").
- GitHub Actions: derleme, `main`'e her push'ta otomatik release.
