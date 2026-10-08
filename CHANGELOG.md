# Değişiklik günlüğü

Sürümler `VERSION` (major.minor) + commit sayısı (patch) ile otomatik verilir; her `main` push'u bir sürümdür.
Burada yalnız kayda değer değişiklikler tutulur.

## 1.0
- Managed kod ayrı bir CLR thread'inde çalışır (ScriptHookVDotNet 3.7 modeli); native'ler doğrudan, oyun thread'inin
  TLS bağlamıyla çağrılır. Fiber kaynaklı rastgele çökmeler ve checkpoint/görev tekrarında runtime'ın kopması
  giderildi. `Runtime.ini` → `ThreadingModel=Fiber` eski davranışa döner.
- `World.GetAllPeds/Vehicles/Objects` artık oyunun durumunu bozmuyor (uzun oyunda varlıkların "kaybolması" düzeldi);
  yeni `World.GetNearbyPeds/Vehicles/Props`.
- İlk StreamEmber dağıtımı: `StreamEmber.Runtime.RDR2.asi` + `StreamEmber.Scripting.RDR2.dll`, `StreamEmber\` klasör
  düzeni (Runtime, Scripts, Config, Logs, Manifests, Licenses), kendi sürüm numaraları.
- `.pdb` ve `.xml` dosyaları dağıtımdan çıkarıldı; ScriptHookRDR2 SDK'sının gereken dosyaları depoda (dış bağımlılık yok).
- Çökme sertleştirmeleri (bkz. README, "Upstream'den farklarımız").
- GitHub Actions: derleme, `main`'e her push'ta otomatik release.
