# Değişiklik günlüğü

Sürümler `VERSION` (major.minor) + commit sayısı (patch) ile otomatik verilir; her `main` push'u bir sürümdür.
Burada yalnız kayda değer değişiklikler tutulur.

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
