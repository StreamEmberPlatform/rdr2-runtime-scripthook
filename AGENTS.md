# StreamEmber Runtime (RDR2) — ajan notları

- Önce `README.md`. Bu repo ScriptHookRDR2DotNet-V2'nin fork'udur; upstream dosyalarını gerekmedikçe değiştirme, değiştirdiğin yeri
  `StreamEmber:` yorumuyla işaretle (upstream birleştirmeleri kolay kalsın).
- Ürün adları ve oyun klasörü yolları yalnız `source/core/StreamEmberLayout.cs` içinde. Başka yerde
  "ScriptHookRDRNetAPI.dll", "scripts" gibi sabit yazma. TargetName (ScriptHookRDRDotNet.vcxproj) ve AssemblyName (API csproj) bu sınıfla aynı olmalı.
- Sürüm `VERSION` + git geçmişinden gelir (`tools/StreamEmber.Build.psm1`); elle sürüm yazma. API `AssemblyVersion`'ı
  API seviyesidir (2.2.0.0), değiştirme.
- Dağıtımda `.pdb` / `.xml` olmaz; CI bunu denetler.
- `tools/StreamEmber.Build.psm1` üç repoda (gtav-runtime-scripthook, rdr2-runtime-scripthook, ui-runtime) aynı tutulur.
- Kullanıcıya görünen metinler Türkçe; kod, tanımlayıcılar ve kod yorumları İngilizce.
- ScriptHookRDR2 SDK'sını depoya ekleme (yeniden dağıtımı yasak); `build.ps1` indirir ve hash'le doğrular. SDK
  sürümü değişirse `build.ps1` içindeki `$SdkName` ve `$SdkHashes` güncellenir.
