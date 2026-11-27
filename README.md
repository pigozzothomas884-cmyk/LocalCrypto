# LocalCrypto

🇮🇹 [Italiano](#italiano) | 🇬🇧 [English](#english)

---

<a id="italiano"></a>
## 🇮🇹 Italiano

Applicazione per Windows (system tray, .NET 8) che cifra cartelle locali e le monta come unità virtuale privata. Nessun server, nessun account, nessun aggiornamento forzato.

### Filosofia

- **Nessun server.** Tutto avviene sul tuo PC. Non esiste un backend che possa raccogliere dati, chiavi o statistiche.
- **Nessun aggiornamento forzato.** Se una versione ti funziona, tienila. Se vuoi aggiornare, scarichi la versione più recente dalla pagina [Releases](https://github.com/pigozzothomas884-cmyk/LocalCrypto/releases) e la installi tu, quando vuoi.
- **Nessun abbonamento.** Software libero con licenza MIT.
- **Vault portabile.** La cartella cifrata è autosufficiente: puoi metterla dove vuoi (disco, chiavetta, cartella sincronizzata su cloud) e sbloccarla da qualsiasi PC con LocalCrypto installato, usando la password o la chiave di recupero.

### Cosa fa

LocalCrypto protegge qualsiasi cartella locale (incluse quelle sincronizzate con servizi cloud) cifrando sia i contenuti sia i nomi e i percorsi dei file. Da sbloccata, la cartella si usa come un normale disco (`V:`–`Z:`), senza condivisioni di rete.

- **Cifratura in-place**: crea un nuovo vault oppure cifra una cartella esistente. I file diventano blob `.lc` e i metadati stanno nel file `.localcrypto`.
- **Crittografia**: AES-256-GCM per contenuti, percorsi relativi e master key; PBKDF2-SHA512 (600.000 iterazioni) con salt casuale per derivare le chiavi da password e chiave di recupero.
- **Montaggio come unità locale** tramite `DefineDosDevice`, con apertura automatica di Esplora File.
- **Sincronizzazione automatica**: le modifiche fatte nell'unità montata vengono ri-cifrate nella cartella protetta.
- **Chiave di recupero** per sbloccare il vault o reimpostare la password.
- **Chiusura sicura**: allo smontaggio o all'uscita l'app sincronizza, rimuove l'unità, azzera la master key in memoria ed elimina la cache temporanea in chiaro.

### Download e installazione

1. Vai alla pagina [Releases](https://github.com/pigozzothomas884-cmyk/LocalCrypto/releases).
2. Scarica l'installer della versione che preferisci.
3. *(Consigliato)* Verifica l'hash SHA-256 del file con quello indicato nella release:
   ```powershell
   Get-FileHash .\LocalCrypto-Setup.exe -Algorithm SHA256
   ```
4. Esegui l'installer.

### Compilare dai sorgenti

Requisiti: Windows e [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (con supporto Windows Forms).

```
dotnet build -c Release
dotnet run
```

### Modello di minaccia

**Da cosa protegge**

- Furto o smarrimento del disco, della chiavetta o del PC spento.
- Accesso di terzi alla cartella sincronizzata su un servizio cloud: chi la copia vede solo blob e nomi cifrati.

**Da cosa NON protegge**

- **Malware o persone con accesso al PC mentre il vault è montato.** Con il vault sbloccato i file sono accessibili in chiaro.
- **Keylogger** che catturano la password mentre la digiti.
- **Contenuto della RAM** durante l'uso.
- **Residui della cache temporanea.** Mentre il vault è montato, i file in uso risiedono in una cache locale in chiaro. In caso di crash o spegnimento improvviso la cache potrebbe non essere eliminata, e sui dischi SSD frammenti di file cancellati possono restare recuperabili.
- **Attacchi alla password.** Chi possiede la cartella del vault può provare a indovinare la password offline. PBKDF2 rallenta l'attacco ma non lo impedisce: usa una passphrase lunga e unica.

LocalCrypto non è stato sottoposto a un audit di sicurezza indipendente. Per dati critici valuta anche strumenti già verificati dalla comunità.

### Avvertenze d'uso

- **Se perdi sia la password sia la chiave di recupero, i dati non sono recuperabili.** Non esiste nessuno che possa resettarli per te.
- **Usa il vault da un solo PC alla volta**, soprattutto se la cartella è sincronizzata su cloud: modifiche simultanee possono generare conflitti.
- **Fai un backup** della cartella cifrata e conserva la chiave di recupero in un luogo separato.

---

<a id="english"></a>
## 🇬🇧 English

A Windows system tray app (.NET 8) that encrypts local folders and mounts them as a private virtual drive. No server, no account, no forced updates.

### Philosophy

- **No server.** Everything happens on your PC. There is no backend that could collect data, keys or statistics.
- **No forced updates.** If a version works for you, keep it. If you want to upgrade, download the latest version from the [Releases](https://github.com/pigozzothomas884-cmyk/LocalCrypto/releases) page and install it whenever you like.
- **No subscription.** Free software under the MIT license.
- **Portable vault.** The encrypted folder is self-contained: put it anywhere (disk, USB stick, cloud-synced folder) and unlock it from any PC with LocalCrypto installed, using your password or recovery key.

### What it does

LocalCrypto protects any local folder (including folders synced with cloud services) by encrypting both file contents and file names/paths. Once unlocked, the folder works like a regular drive (`V:`–`Z:`), with no network shares involved.

- **In-place encryption**: create a new vault or encrypt an existing folder. Files become `.lc` blobs, and metadata lives in the `.localcrypto` file.
- **Cryptography**: AES-256-GCM for contents, relative paths and the master key; PBKDF2-SHA512 (600,000 iterations) with a random salt to derive keys from the password and the recovery key.
- **Mounting as a local drive** via `DefineDosDevice`, automatically opening File Explorer.
- **Automatic sync**: changes made on the mounted drive are re-encrypted into the protected folder.
- **Recovery key** to unlock the vault or reset the password.
- **Safe shutdown**: when unmounting or exiting, the app syncs, removes the drive, zeroes the master key in memory and deletes the temporary plaintext cache.

### Download and install

1. Go to the [Releases](https://github.com/pigozzothomas884-cmyk/LocalCrypto/releases) page.
2. Download the installer for the version you prefer.
3. *(Recommended)* Verify the file's SHA-256 hash against the one listed in the release:
   ```powershell
   Get-FileHash .\LocalCrypto-Setup.exe -Algorithm SHA256
   ```
4. Run the installer.

### Build from source

Requirements: Windows and the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (with Windows Forms support).

```
dotnet build -c Release
dotnet run
```

### Threat model

**What it protects against**

- Theft or loss of a disk, USB stick or powered-off PC.
- Third-party access to a folder synced to a cloud service: whoever copies it sees only encrypted blobs and names.

**What it does NOT protect against**

- **Malware or people with access to your PC while the vault is mounted.** With the vault unlocked, files are accessible in plaintext.
- **Keyloggers** that capture your password as you type it.
- **RAM contents** during use.
- **Leftovers of the temporary cache.** While the vault is mounted, files in use live in a plaintext local cache. After a crash or sudden shutdown the cache may not be deleted, and on SSDs fragments of deleted files may remain recoverable.
- **Password attacks.** Anyone holding the vault folder can try to guess the password offline. PBKDF2 slows an attack down but does not prevent it: use a long, unique passphrase.

LocalCrypto has not undergone an independent security audit. For critical data, consider also community-vetted tools.

### Usage warnings

- **If you lose both your password and your recovery key, your data is unrecoverable.** There is nobody who can reset them for you.
- **Use the vault from one PC at a time**, especially if the folder is cloud-synced: simultaneous edits can cause conflicts.
- **Back up** the encrypted folder and store the recovery key in a separate place.

---

## Licenza / License

Distribuito sotto licenza MIT. / Distributed under the MIT license. Vedi / See [LICENSE](LICENSE).
