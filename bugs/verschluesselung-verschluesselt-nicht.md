# `DataEncryptionService` verschlüsselt nicht — es legt den Klartext ab und meldet `AES256GCM`

- **Girder-Fassung:** 4.4.0
- **Gefunden beim:** Messung der Geheimnisfrage (PBI-8.2 / H2), `docs/MESSUNG-GEHEIMNISFRAGE.md`
- **Art:** Fehler
- **Blockiert:** nein — wir benutzen `AddEncryption` nicht, und nach dieser
  Messung werden wir es auch nicht. Es blockiert jeden anderen, der es benutzt.

## Was passiert

`Girder.Redis.Security.Encryption.DataEncryptionService` ist die **einzige**
ausgelieferte Umsetzung von `IDataEncryptionService`; `AddEncryption()` verlangt
sie namentlich (*„call AddRedisEncryption() from Girder.Redis"*).

Sie verschlüsselt nichts. `EncryptAesGcmAsync` legt den Klartext unverändert in
den Ergebnispuffer, setzt eine Prüfsumme aus lauter Nullbytes und verpackt
beides base64-kodiert in eine JSON-Hülle, die `"Algorithm":"AES256GCM"` trägt.
Der gewürfelte Vektor wird gespeichert und nie benutzt. Daneben liegt der
SHA-256 **des Klartexts** als `IntegrityHash`.

Gemessen gegen Redis 8, mit `AddConfiguredMasterKey()` und einem echten
32-Byte-Hauptschlüssel:

```
Success=True  Algorithm=AES256GCM  Fehler=(keiner)
{"Version":"1.0","KeyId":"key_0fe5…","Algorithm":"AES256GCM",
 "IV":"C9eTgM5VJKDX+6WU","AuthTag":"AAAAAAAAAAAAAAAAAAAAAA==",
 "Data":"c2stYW50LUdFSEVJTU5JUy1EQVMtTklFTUFORC1TRUhFTi1EQVJG", …,
 "IntegrityHash":"d9iC+fmJfvnQBQd4okCXYu275NSYZ/Pi2pxWOvJbmKo=", …}

Feld Data dekodiert                   : sk-ant-GEHEIMNIS-DAS-NIEMAND-SEHEN-DARF
AuthTag ist lauter Null               : True
IntegrityHash = SHA-256 des KLARTEXTS : True
Entschluesseln mit FREMDEM Schluessel : Success=True IntegrityVerified=True
                                        Data=sk-ant-GEHEIMNIS-DAS-NIEMAND-SEHEN-DARF
```

Erwartet war ein Geheimtext, der den Klartext nicht enthält, eine echte
GCM-Prüfsumme, und eine Absage, wenn mit einem fremden Schlüssel
entschlüsselt wird.

Dieselben drei Stellen betreffen `ChaCha20Poly1305` (leitet auf denselben Weg
um) und `CompressData`/`DecompressData` (geben ihre Eingabe zurück).

## Warum es Girders ist

Kein WorkerTransfer-Code, nur Girder-Pakete von nuget.org und ein
Wegwerf-Redis:

```bash
docker run -d --rm --name probe-redis -p 6399:6379 redis:8-alpine
```

```csharp
// Pakete: Girder.Infrastructure 4.4.0, Girder.Redis 4.4.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Girder.Abstractions.Hosting;
using Girder.Abstractions.Security.Encryption;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security.Encryption;
using Girder.Redis;
using Girder.Redis.Security;

var b = WebApplication.CreateBuilder();
b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Encryption:MasterKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
});

b.Services.AddRedisConnection("127.0.0.1:6399", "probe");
b.Services.AddRedisEncryption();
b.Services.AddConfiguredMasterKey();
b.Services.AddGirder(b.Configuration, b.Environment, "probe",
    g => g.Use(GirderModule.Encryption));

var app = b.Build();
await app.StartAsync();

var verwaltung = app.Services.GetRequiredService<IKeyManagementService>();
var chiffre = app.Services.GetRequiredService<IDataEncryptionService>();

var keyId = await verwaltung.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);
var e = await chiffre.EncryptWithKeyAsync("sk-ant-GEHEIMNIS", keyId);

var huelle = Encoding.UTF8.GetString(Convert.FromBase64String(e.EncryptedData));
using var doc = JsonDocument.Parse(huelle);

Console.WriteLine(Encoding.UTF8.GetString(
    Convert.FromBase64String(doc.RootElement.GetProperty("Data").GetString()!)));
// -> sk-ant-GEHEIMNIS        (erwartet: Geheimtext)

// und mit einem FREMDEN Schluessel:
var fremd = await verwaltung.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);
var d = await chiffre.DecryptWithKeyAsync(e.EncryptedData, fremd);
Console.WriteLine($"{d.Success} {d.IntegrityVerified} {d.Data}");
// -> True True sk-ant-GEHEIMNIS        (erwartet: Success=false)
```

Die Stelle sagt es selbst:

```csharp
// Girder.Redis/Security/DataEncryptionService.cs:623
// Perform GCM encryption (simplified - in production use proper GCM implementation)
Array.Copy(data, encryptedData, data.Length);
```

**Und der Zusage widerspricht es an drei Stellen.** `GirderModule.Encryption`
sagt *„Encrypting values at rest."* `IMasterKeyProvider` sagt *„That is what
makes customer key sovereignty possible."* `EncryptionResult.EncryptedData`
sagt *„Encrypted data (Base64 encoded)."* — es ist Base64, aber nicht
verschlüsselt.

**Warum die eigenen Tests es nicht merken**, denn das ist der eigentlich
interessante Teil: `DataEncryptionServiceTests` prüft `result.Success`, die
Fehlertexte und einen Hin-und-Rückweg; die einzige Aussage über den Geheimtext
ist `EncryptedData.Should().NotBeNullOrEmpty()`. Ein Hin-und-Rückweg ist
trivial grün, wenn nichts passiert — kein Test vergleicht den Geheimtext je mit
dem Klartext, und keiner versucht, mit einem fremden Schlüssel zu
entschlüsseln.

## Nebenbefund im selben Bereich: ein Anbieter, den niemand registriert

`AddSecretStoreMasterKey()` ist die Fassung, die die XML-Doku ausdrücklich
**empfiehlt** (*„prefer AddSecretStoreMasterKey(), which reads the key from a
secret store you run"*). Sie löst `ISecretProvider` auf — und **kein
Girder-Modul registriert je einen**. `SecureSecretManager` baut die vier
Anbieter intern und ist selbst nirgends verdrahtet.

Der Dienst startet trotzdem: die Anbieterprüfung sieht `IMasterKeyProvider` und
ist zufrieden.

```
START Encryption + AddSecretStoreMasterKey: LAEUFT AN
   erste Benutzung -> InvalidOperationException: No service for type
      'Girder.Abstractions.Security.Secrets.ISecretProvider' has been registered.
```

Das ist genau der Ausfallmodus, den `ProviderRequirementValidator` abschaffen
soll: *„Without this the container resolves lazily, so a missing provider
surfaces on the first request that happens to need it."* Der empfohlene Weg
fällt durch dieses Netz, weil die Lücke eine Ebene tiefer liegt.
**Art: Lücke.**

## Was es uns kostet

Heute nichts: `AddEncryption` steht bei uns draussen, und
`src/shared/WorkerTransfer.ServiceDefaults/Geheimnisspeicher.cs` macht die
Arbeit selbst — AES-256-GCM, ein gewürfelter Vektor je Geheimnis, Verfälschung
fällt auf, Hauptschlüssel aus der Umgebung. Nachgemessen am selben Tag mit
derselben Probe: *„Klartext drin? nein."*

Es kostet uns die **Option**: solange das steht, ist `AddEncryption` keine
Antwort auf eine Frage nach Feldverschlüsselung, und die Begründung in
`Dienstgrundlage.cs` darf sich nicht auf „wir haben noch nicht entschieden, wo
der Hauptschlüssel liegt" stützen — entschieden ist es, und das Modul wäre
trotzdem falsch.

Ein Umweg ist nicht gebaut und wird nicht gebraucht.

## Stand

- [ ] gemeldet
- [ ] in Girder behoben, Fassung: <…>
- [ ] Umweg hier entfernt — es gibt keinen
