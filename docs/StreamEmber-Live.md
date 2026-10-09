# StreamEmber Live — canlı yayın modları

Canlı yayınla çalışan modlar (hediye, yorum, beğeni → oyunda aksiyon) çalışma ortamının script API'sini kullanır:
`StreamEmber.Scripting.GTAV.dll` / `StreamEmber.Scripting.RDR2.dll`, namespace `StreamEmber.Live`. Kimlik, EventFabric
bağlantısı, Falcon ayarları ve presence API'nin içindedir; mod HTTP/WebSocket yazmaz, ayrı bir core DLL'i taşımaz.

Kod iki oyunda aynıdır (`source/scripting_v3/StreamEmber.Live/`); oyuna özgü tek dosya
`source/scripting_v3/StreamEmber.Live.Game/GameBridge.cs` (taban sınıf `GTA.Script` / `RDR2.Script`, bildirim, dil, log).

## Mod yazmak

Yeni mod `StreamEmberPlatform/gtav-mod-template` / `rdr2-mod-template` şablonundan başlar (yapı, derleme, kurallar, AI rehberi `AGENTS.md`).

```csharp
using StreamEmber.Live;

public sealed class JohnWick : LiveScript          // LiveScript : GTA.Script (RDR2'de RDR2.Script)
{
    private int _maxHumans = 40;

    public JohnWick()
    {
        Tick += OnTick;                                                     // her zamanki gibi
        On("player.heal", action => Game.Player.Character.Health = 1000);  // aksiyon → işleyici
        SettingsChanged += (s, e) => _maxHumans = e.Settings.GetInt("battle.maxHumans", 40, 1, 100);
        ActionReceived += (s, e) => { /* her aksiyon; e.Handled = yukarıdaki işleyici çalıştı mı */ };
    }

    [LiveAction("enemy.spawn")]                                             // ya da öznitelikle
    private void SpawnEnemies(LiveAction action)
    {
        int count = (int)Math.Min(50L, (long)action.Arguments.GetInt("count", 1, 1, 50) * action.Quantity);
        string name = action.Viewer.DisplayName;                            // izleyici
        // action.Trigger.Platform / Target (yayıncı) / Kind, action.Gift?.Name, action.Viewer.AvatarUrl …
    }
}
```

Bütün olaylar ve işleyiciler script thread'inde, Tick gibi çalışır; oyun API'si güvenle çağrılır.

### Gömülü kaynaklar (zorunlu)

```xml
<ItemGroup>
  <EmbeddedResource Include="resources\streamember-module.json" LogicalName="streamember-module.json" />
  <EmbeddedResource Include="resources\game-schema.json" LogicalName="game-schema.json" />
</ItemGroup>
```

- `streamember-module.json`: `{ "applicationUuid": "<Falcon oyun UUID'si>", "code": "john-wick", "name": "John Wick" }`
- `game-schema.json`: Falcon'daki oyun şeması (`actions[]`: id, label, arguments[].default; `settings.fields[]`: path, default).
  Argüman ve ayar varsayılanları buradan gelir.

## API

| Tür | Görev |
|---|---|
| `LiveScript` | Modun taban sınıfı. `On(id, handler)`, `[LiveAction(id)]`; olaylar `LiveStarted`, `LiveStopped`, `ActionReceived`, `SettingsChanged`, `ConnectionChanged`; `Manifest`, `IsLiveActive` |
| `LiveAction` | GameAction v2 paketi: `Action`, `Arguments`, `Quantity`, `Trigger`, `Viewer`, `Gift`, `Comment`, `LikeCount`, `Subscription`, `Reward`, `Aggregation`, `IsTest`, `Render("{user} {gift}…")`, `Describe()`, `ToJson()` |
| `LiveTrigger` | `Platform`, `Target` (yayıncının kullanıcı adı), `RoomId`, `Kind`, `Category`, `Event`, `EventId` |
| `LiveViewer` | `Id`, `Username`, `Nickname`, `AvatarUrl`, `DisplayName` |
| `LiveGift`, `LiveSubscription`, `LiveReward`, `LiveAggregation` | Tetikleyici ayrıntıları (yoksa null) |
| `LiveValues` | Ayar/argüman okuyucu: noktalı yol, `GetInt/GetDouble` (sınırlı), `GetBool`, `GetString`, `GetStringList`, `GetObject` |
| `LiveManifest` | Gömülü manifest + şema: `ApplicationUuid`, `Code`, `Name`, `ActionIds`, `SettingDefaults`, `ActionLabel` |
| `ModLog` | `Info`, `Warn`, `Error`, `Debug` → `Runtime.log` (`[Mod:<code>]`); her thread'den güvenli |
| `LiveSession` | `IsActive`, `CustomerUuid`, `ApplicationUuid`, `IsConnected`, `CanWrite`, `Settings`, `SettingsRevision`, `Language`, `ReloadSettings()`, `ResetGCore(done)` |

Birden çok `LiveScript` aynı modülde olabilir (aynı derleme = aynı manifest); hepsi aksiyonları alır.
Birden çok modül kuruluysa etkin olan `Runtime.ini` → `LiveModule` ile seçilir (boşsa ilk yüklenen).

## Kimlik (Identity v2)

| İş | Kimlik |
|---|---|
| Aksiyonları dinleme (`/events/ws`), ayarları okuma (Falcon `/public/game/config`) | Yalnız customer UUID. Elde runtime token varsa yanına eklenir (reddedilirse tokensız denenir) |
| EventFabric'e yazma: presence, GCore reset | Runtime token zorunlu (`Authorization: Bearer`). Token yoksa yazılmaz |

- Customer UUID: `LiveCustomerUuid` (yalnız geliştirme) → Launcher `GET /api/customer` → token `sub`.
- Token: Launcher `POST /api/runtime-token` `{ applicationUuid, audience: "eventfabric" }` → `STREAMEMBER_RUNTIME_TOKEN`
  (Launcher başlatırken verir, 10 dk). Bitişine ~2 dk kala yenilenir; 401'de atılıp yeniden istenir.
- Launcher adresi: `LiveLauncherUrl` → `STREAMEMBER_LOCAL_API` → `http://127.0.0.1:47880`.
- Token hiçbir zaman loglanmaz; istemci token'ı doğrulamaz (yalnız `sub`/`app`/`exp` okunur), doğrulama sunucudadır.

## Akış

1. İlk `LiveScript` tick'inde `Runtime.ini` okunur, modül seçilir, şema varsayılanlarıyla `LiveStarted` + `SettingsChanged`.
2. Customer UUID bulunur → EventFabric WebSocket (cursor ile replay, 1→30 sn geri çekilme, 20 sn ping, 4096 id tekrar eleme).
3. Falcon ayarları hemen ve 60 sn'de bir okunur; değişince `SettingsChanged`.
4. 15 sn'de bir: presence (token ile), Launcher'a `/api/active-application`, customer yeniden kontrolü.
5. Gelen aksiyon: başka oyuna aitse atılır, şema argüman varsayılanları eklenir, ekranda duyurulur, işleyiciler + `ActionReceived`.

## Test kısayolları (`LiveTestHotkeys=true`)

Ctrl+Shift+F1–F9 şemanın ilk dokuz aksiyonunu test paketiyle çalıştırır, F10 durum, F11 ayarları yeniden okur,
F12 GCore'u sıfırlar. Günlük satırları `StreamEmber\Logs\Runtime.log` içinde `[Live]` önekiyle.
