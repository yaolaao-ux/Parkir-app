# Remediation Status

## Gelombang

Gelombang 0 — Baseline dan secret hygiene.

## Baseline

- Repository: `/root/Parkir-app`
- Branch kerja: `chore/remediation-baseline`
- Commit awal: `508b805` (`Harden POS checkout and backend operations`)
- Commit akhir: branch head setelah commit dokumen Gelombang 0 (lihat `git log -1 --oneline`)
- Working tree saat baseline: bersih
- Dokumen acuan: `PARKIR_APP_REMEDIATION_PLAN.md` diberikan melalui prompt; file tersebut belum ada di repository saat baseline
- SDK: .NET 8.0.428 (linux-arm64)
- Node: v22.22.1
- PostgreSQL client: 18.6; server database lokal tidak berjalan pada `127.0.0.1:5432`
- PlatformIO: tidak terpasang pada environment ini

### Inventaris singkat

- API: `backend/Parkir.Api` (ASP.NET Core .NET 8)
- POS: `pos/Parkir.Pos` (WPF, target Windows)
- Dashboard: `backend/Parkir.Api/wwwroot`
- Database: PostgreSQL; 7 tabel (`vehicle_types`, `members`, `users`, `audit_logs`, `devices`, `gates`, `transactions`)
- Migration: `database/migrations/002`–`009`; dijalankan otomatis oleh `DatabaseInitializer`
- Firmware: `firmware/gate_in/gate_in.ino` + `platformio.ini`
- Test: `tests/Parkir.Tests` (runner konsol)
- CI: `.github/workflows/build-pos.yml`, `docker.yml`, `tests.yml`, dan `security.yml` (baru)

## Temuan yang diverifikasi

| ID | Status | Bukti ringkas |
|---|---|---|
| R-01 — kredensial demo | **CONFIRMED, partial** | README current tree sudah bersih dari password demo. Namun riwayat Git masih memiliki penanda `Password:` pada beberapa commit lama (`afec37f`, `be337ea`, `91b0203`, `80c01a3`, dan commit sebelumnya). Nilai tidak ditampilkan. |
| R-02 — fallback password DB | **CONFIRMED** | `backend/Parkir.Api/Program.cs` masih memakai connection string fallback dengan kredensial default bila konfigurasi tidak tersedia. Dijadwalkan Gelombang 2. |
| R-03 — port PostgreSQL terbuka | **CONFIRMED** | `docker-compose.yml` masih memetakan `5432:5432`. Dijadwalkan Gelombang 2. |
| R-04 — tarif tidak disnapshot | **FIXED di 508b805** | Kolom snapshot tarif ada di `transactions`; `ParkingRepository` memakai `COALESCE` snapshot; test 24/25 jam lulus. |
| R-05 — entry tidak idempotent | **FIXED di 508b805** | `EntryRequest.IdempotencyKey`, unique index per gate, dan pengembalian tiket lama; test kontrak JSON lulus. Integration DB belum dijalankan. |
| R-06 — concurrency checkout | **PARTIAL** | Update checkout memakai `WHERE payment_status = 'UNPAID'` dan idempotency key; belum ada integration test dua kasir bersamaan dengan PostgreSQL nyata. |
| R-07 — audit non-atomic | **PARTIAL** | Checkout dan void menulis audit dalam transaksi yang sama. Mutasi admin (tarif/member/gate) masih menulis audit melalui repository terpisah. |
| R-08 — anti-passback | **CONFIRMED belum ada** | Belum ada constraint/pemeriksaan sesi aktif ganda per member/kendaraan. Dijadwalkan Gelombang 1/5. |
| R-09 — entropi tiket rendah | **CONFIRMED** | Ticket ID memotong GUID menjadi 8 karakter hex (32 bit) melalui `[..21]` di `ParkingService`. Dijadwalkan Gelombang 1. |
| R-10/R-12 — API key perangkat bersama | **CONFIRMED** | Endpoint perangkat memakai satu `X-API-Key` global; belum ada kredensial unik/revocation per device. Dijadwalkan Gelombang 2. |
| R-11 — session lifecycle | **PARTIAL** | JWT 8 jam, rate limit login, dan POS re-login sudah ada; belum ada revocation/disable user end-to-end test. |
| R-13 — printer/ticket state | **PARTIAL** | Firmware mempertahankan idempotency key sampai print sukses; status `PRINT_CONFIRMED` fisik belum tersedia. Dijadwalkan Gelombang 3. |
| R-17 — offline queue | **PARTIAL** | Status outbox, backoff, dead-letter, dan tombol tindakan sudah ada; rekonsiliasi manual penuh dan test DB queue belum ada. |
| R-19 — timezone | **PARTIAL** | `BusinessClock` dengan `Business:TimeZone` default `Asia/Jakarta`; lokasi operasional belum dipastikan. Dijadwalkan Gelombang 5. |
| R-20 — automated tests | **PARTIAL** | 21 regression test lulus; integration/E2E/hardware tests belum tersedia. |

Temuan yang tidak lagi relevan pada commit baseline:

- Password demo di README current tree sudah dihapus.
- Trailing-slash API key bypass sudah ditutup dan diuji.
- Timezone report/dashboard tidak lagi memakai `CURRENT_DATE`/UTC C#.
- POS outbox tidak lagi memblokir antrean pada 4xx pertama.

Temuan yang belum dapat diverifikasi:

- Migration runner terhadap PostgreSQL nyata.
- Perilaku WPF pada Windows.
- Perilaku firmware pada PlatformIO/hardware.
- Backup/restore dan monitoring produksi.

## Perubahan yang dilakukan

| File | Ringkasan |
|---|---|
| `.gitignore` | Menambahkan pola file secret/private key, database lokal, dan file sensitif. |
| `tools/scan-secrets.sh` | Pemindai secret dependency-free untuk tracked files; hanya mencetak nama file, bukan nilai. |
| `.github/workflows/security.yml` | Menjalankan secret scan pada push, pull request, dan manual. |
| `REMEDIATION_STATUS.md` | Dokumen status Gelombang 0 ini. |

Tidak ada perubahan source code API/POS/firmware, migration, atau data pada Gelombang 0.

## Database

- Migration: tidak ada migration baru pada Gelombang 0.
- Dampak data: tidak ada.
- Cara menjalankan: tidak applicable.
- Cara rollback/pemulihan: tidak applicable.

## Pengujian

| Test | Perintah | Hasil |
|---|---|---|
| Build API Release | `dotnet build backend/Parkir.Api/Parkir.Api.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Build POS WPF Release | `dotnet build pos/Parkir.Pos/Parkir.Pos.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Regression tests | `dotnet run --project tests/Parkir.Tests/Parkir.Tests.csproj --configuration Release` | **PASS** — 21/21 |
| Secret scan current tree | `bash tools/scan-secrets.sh` | **PASS** — tidak ada pola secret berisiko tinggi pada tracked files |
| Secret scan riwayat Git | pemeriksaan penanda `Password:` pada `README.md` historis | **FAIL/BLOCKED** — kredensial lama terdapat di riwayat; perlu keputusan pemilik |
| Integration tests PostgreSQL | belum dijalankan | **NOT RUN** — server database tidak berjalan |
| WPF runtime/visual | belum dijalankan | **NOT RUN** — environment Linux |
| Firmware build | `pio run` | **BLOCKED** — PlatformIO tidak terpasang |

## Acceptance criteria Gelombang 0

| Kriteria | Status |
|---|---|
| Tidak ada secret aktif baru di current tree | **PASS** — pemindaian pola high-signal dan review README |
| Secret scanning tersedia di CI | **PASS** — workflow dan script ditambahkan |
| Kredensial yang pernah terekspos dirotasi | **BLOCKED** — membutuhkan akses database/layanan dan persetujuan pemilik |
| Riwayat Git dibersihkan dari kredensial lama | **BLOCKED** — force push/rewrite history dilarang tanpa persetujuan eksplisit |
| Baseline build/test tercatat | **PASS** |
| Working tree bersih sebelum perubahan | **PASS** |
| Konfigurasi development tidak terganggu | **PASS** — hanya file ignore/CI/dokumentasi yang berubah |

## Risiko tersisa

1. **R-01 (P0):** password demo lama masih ada di riwayat Git. Rotasi password dan pembersihan history memerlukan koordinasi pemilik repository.
2. **R-02 (P0):** fallback connection string default masih ada; aplikasi production harus gagal start tanpa secret. Akan dikerjakan Gelombang 2.
3. **R-03 (P0):** port PostgreSQL masih dipublikasikan di Compose. Akan dikerjakan Gelombang 2.
4. **R-08/R-09 (P0/P1):** anti-passback dan entropi ticket ID belum diperbaiki; Gelombang 1.
5. Integration test PostgreSQL, WPF runtime, dan hardware belum dijalankan; hasil build tidak boleh dianggap bukti siap operasional.

## Tindakan yang membutuhkan persetujuan

- Rotasi kredensial akun/database yang pernah memakai password demo.
- Pembersihan riwayat Git dan force push (dilarang pada prompt ini).
- Penyediaan database development/test PostgreSQL untuk integration test.
- Pengujian WPF pada Windows.
- Pengujian firmware dan keselamatan gate pada hardware.

## Rekomendasi gelombang berikutnya

Gelombang 1 — Transaction integrity: verifikasi dan selesaikan anti-passback, atomic audit untuk mutasi admin, penguatan entropi ticket ID, serta integration test PostgreSQL untuk entry/checkout idempotency dan concurrency.

---

# Gelombang 1 — Transaction integrity

## Baseline

- Branch kerja: `fix/transaction-integrity`
- Commit awal: `602e557` (`chore: add remediation baseline and secret scan`)
- Commit akhir: branch head setelah commit Gelombang 1 (lihat `git log -1 --oneline`)
- Working tree saat baseline: bersih
- Migration baru: `010_add_anti_passback_index.sql`

## Temuan yang diverifikasi

| ID | Status | Bukti ringkas |
|---|---|---|
| R-04 — snapshot tarif | **FIXED** | Snapshot sudah ada sejak `508b805`; kalkulasi memakai snapshot sesi. |
| R-05 — entry idempotency | **FIXED + diperkuat** | Key per gate, fingerprint payload, dan transaksi database; retry dengan payload berbeda ditolak `409`. |
| R-06 — checkout/payment concurrency | **PARTIAL** | Update memakai predikat `UNPAID` + `time_out IS NULL`, idempotency checkout, dan unique `checkout_id`. Integration test PostgreSQL dua kasir belum dijalankan. |
| R-07 — audit non-atomic | **FIXED untuk mutasi admin** | Gate, vehicle type, dan member mutation + audit ditulis dalam satu transaksi di `AdminRepository`. Checkout/void sudah atomic. |
| R-08 — anti-passback | **PARTIAL** | Unique partial index `ux_transactions_active_member` dan `SELECT ... FOR UPDATE` per member. Berlaku untuk entry RFID/member; kendaraan non-member belum memiliki identitas plat di entry. |
| R-09 — entropi tiket | **FIXED** | `TicketIdGenerator` memakai GUID 128-bit penuh; panjang ID 45 karakter; test 1.000 ID unik lulus. |

## Perubahan yang dilakukan

| File | Ringkasan |
|---|---|
| `database/migrations/010_add_anti_passback_index.sql` | Preflight duplikat sesi member, unique partial index anti-passback, kolom fingerprint entry. |
| `database/schema.sql` | Index anti-passback dan kolom `entry_request_fingerprint`. |
| `backend/Parkir.Api/Services/TicketIdGenerator.cs` | Generator ticket ID 128-bit dengan tanggal bisnis. |
| `backend/Parkir.Api/Services/EntryRequestFingerprint.cs` | Fingerprint kanonik gate/vehicle/RFID, tanpa foto agar retry foto baru tetap valid. |
| `backend/Parkir.Api/Services/IdempotencyConflictException.cs` | Exception khusus untuk konflik key/payload. |
| `backend/Parkir.Api/Services/ParkingService.cs` | Memakai generator/fingerprint, logging konflik, dan validasi yang sudah ada. |
| `backend/Parkir.Api/Data/ParkingRepository.cs` | Transaksi entry, row lock member, anti-passback check, fingerprint, dan commit/rollback eksplisit. |
| `backend/Parkir.Api/Data/AdminRepository.cs` | Mutasi gate/vehicle/member dan audit dalam satu transaksi. |
| `backend/Parkir.Api/Services/AdminService.cs` | Menghapus audit terpisah; meneruskan actor/source ke repository. |
| `backend/Parkir.Api/Controllers/ParkingController.cs` | Konflik idempotency entry mengembalikan `409`. |
| `tests/Parkir.Tests/Program.cs` | Test ticket ID dan fingerprint; jumlah migration menjadi 9. |
| `README.md` | Migration runner mencakup `002`–`010`. |

## Database

- Migration: `010_add_anti_passback_index.sql`.
- Dampak data: menambah kolom nullable dan unique partial index; preflight gagal jika sudah ada lebih dari satu sesi aktif untuk member yang sama.
- Cara menjalankan: otomatis oleh `DatabaseInitializer` saat API start; backup sebelum deploy.
- Cara rollback/pemulihan: drop index/kolom hanya setelah backup dan persetujuan; migration bersifat forward-only. Jika preflight gagal, selesaikan sesi duplikat terlebih dahulu, jangan menghapus data.

## Pengujian

| Test | Perintah | Hasil |
|---|---|---|
| Build API Release | `dotnet build backend/Parkir.Api/Parkir.Api.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Build POS WPF Release | `dotnet build pos/Parkir.Pos/Parkir.Pos.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Regression tests | `dotnet run --project tests/Parkir.Tests/Parkir.Tests.csproj --configuration Release` | **PASS** — 23/23 |
| Secret scan | `bash tools/scan-secrets.sh` | **PASS** |
| Integration tests PostgreSQL | belum dijalankan | **NOT RUN** — server database tidak tersedia |
| Firmware build | `pio run` | **BLOCKED** — PlatformIO tidak terpasang |

## Acceptance criteria Gelombang 1

| Kriteria | Status |
|---|---|
| Retry entry dengan key sama tidak membuat session baru | **PARTIAL** — kode dan kontrak diuji; DB integration NOT RUN |
| Key sama dengan payload berbeda ditolak | **PASS** — fingerprint + `409`; unit test lulus |
| Perubahan tarif tidak mengubah sesi aktif | **PASS** — snapshot code + unit test tarif; DB integration NOT RUN |
| Dua kasir tidak dapat menyelesaikan sesi yang sama | **PARTIAL** — invariant SQL ada; DB concurrency test NOT RUN |
| Mutasi admin + audit atomic | **PARTIAL** — transaksi kode terverifikasi build; DB rollback test NOT RUN |
| Anti-passback member | **PARTIAL** — index + lock kode; DB integration NOT RUN |
| Ticket ID kuat | **PASS** — unit test 128-bit/45 karakter |
| Regression test lulus | **PASS** — 23/23 |

## Risiko tersisa

1. **Integration test PostgreSQL belum dijalankan**; migration 010 dan concurrency belum dibuktikan pada database nyata.
2. **Anti-passback non-member belum ada** karena entry belum memiliki identitas plat/RFID; perlu keputusan desain bila kendaraan non-member harus dibatasi.
3. **Payment lifecycle** belum memiliki status pending/refund; model saat ini final `PAID/MEMBER/LOST_TICKET/VOID`.
4. **Audit login** masih ditulis terpisah dari autentikasi (tidak ada mutasi bisnis); tidak termasuk atomic mutation admin.
5. Migration 010 akan menolak startup bila database sudah memiliki sesi aktif ganda; ini disengaja agar data tidak rusak, tetapi memerlukan prosedur rekonsiliasi operator.

## Tindakan yang membutuhkan persetujuan

- Menyediakan database test PostgreSQL untuk integration test dan uji migration dari schema lama.
- Menentukan aturan anti-passback untuk kendaraan non-member.
- Menentukan apakah status payment pending/refund diperlukan sebelum pilot.

## Rekomendasi gelombang berikutnya

Gelombang 2 — Security hardening: hapus fallback connection string, batasi port database, aktifkan TLS sesuai topologi, kredensial unik per perangkat, revocation, dan pengujian authorization.

---

# Gelombang 2 — Security hardening

## Baseline

- Branch kerja: `fix/security-hardening`
- Commit awal: `d104f1e` (`fix: harden transaction integrity and anti-passback`)
- Commit akhir: branch head setelah commit Gelombang 2 (lihat `git log -1 --oneline`)
- Working tree saat baseline: bersih
- Migration baru: `011_add_device_credentials_and_user_status.sql`

## Temuan yang diverifikasi/diperbaiki

| ID | Status | Bukti ringkas |
|---|---|---|
| R-01/R-02 — fallback kredensial | **FIXED** | `ConnectionStrings:Parking` wajib; API gagal start tanpa konfigurasi. Tidak ada fallback password database. |
| R-03 — port PostgreSQL | **FIXED** | Port `5432` dihapus dari Docker Compose; akses host hanya melalui `docker compose exec`. |
| R-10/R-12 — API key bersama | **FIXED (kode)** | Kredensial perangkat unik di `device_credentials`; lookup per `X-Device-Id`; key disimpan sebagai PBKDF2 hash; rotasi/revocation melalui admin. |
| R-12 — impersonasi gate | **FIXED (kode)** | Entry memvalidasi `GateId` = device terautentikasi; heartbeat memvalidasi `deviceId` payload = device terautentikasi. |
| R-11 — user lifecycle | **PARTIAL** | Kolom `users.is_active`, login hanya untuk user aktif, endpoint admin untuk disable/enable. JWT yang sudah terbit belum dapat dicabut sebelum expiry. |
| Security headers/rate limit | **FIXED** | CSP, nosniff, frame deny, referrer/permissions policy; rate limit endpoint perangkat dan login. |
| TLS | **PARTIAL/BLOCKED** | Konfigurasi `Security:RequireHttps` + dokumentasi reverse proxy; sertifikat/TLS aktual belum diuji. |

## Perubahan yang dilakukan

| File | Ringkasan |
|---|---|
| `database/migrations/011_add_device_credentials_and_user_status.sql` | Tabel `device_credentials`, index aktif, kolom `users.is_active`. |
| `database/schema.sql` | Skema kredensial perangkat dan status user. |
| `backend/Parkir.Api/Security/ApiKeyAuthenticationHandler.cs` | Autentikasi per device + claim `device_id`; key PBKDF2; lookup DB. |
| `backend/Parkir.Api/Security/DeviceKeyHasher.cs` | Generate/hash/verify key perangkat dengan PBKDF2-SHA256 dan fixed-time compare. |
| `backend/Parkir.Api/Data/DeviceCredentialRepository.cs` | Issue/rotate/revoke kredensial + audit atomik. |
| `backend/Parkir.Api/Data/AdminRepository.cs` | Status user + audit atomik. |
| `backend/Parkir.Api/Services/AdminService.cs` | Provisioning/revocation kredensial dan status user. |
| `backend/Parkir.Api/Controllers/AdminController.cs` | Endpoint credential perangkat dan status user. |
| `backend/Parkir.Api/Controllers/ParkingController.cs` | Device binding untuk entry/heartbeat dan rate limit. |
| `backend/Parkir.Api/Controllers/MediaController.cs` | Rate limit upload masuk. |
| `backend/Parkir.Api/Program.cs` | Hapus fallback DB/global API key; security headers; HTTPS config; rate limit device. |
| `backend/Parkir.Api/Data/AuthRepository.cs` | Tolak user nonaktif saat login. |
| `firmware/gate_in/gate_in.ino` | Header `X-Device-Id` untuk entry/media/heartbeat; key per gate. |
| `pos/Parkir.Pos/PosSettings.cs` | `PARKIR_DEVICE_KEY`/`PARKIR_DEVICE_ID` helpers. |
| `pos/Parkir.Pos/MainWindow.xaml.cs` | Header device untuk API client; heartbeat memakai device ID yang sama. |
| `pos/Parkir.Pos/LoginWindow.xaml.cs` | Device key baru dengan fallback env lama. |
| `tests/Parkir.Tests/Program.cs` | Test PBKDF2 device key; migration count 10; test auth tanpa header tetap `401`. |
| `README.md`, `.env.example`, `docker-compose.yml` | Dokumentasi kredensial perangkat, port DB internal, TLS, env baru. |

## Database

- Migration: `011_add_device_credentials_and_user_status.sql`.
- Dampak data: menambah tabel kredensial dan kolom status user; tidak menghapus data.
- Cara menjalankan: otomatis oleh `DatabaseInitializer`; backup sebelum deploy.
- Rollback: aplikasi lama dapat dijalankan sebelum kredensial diaktifkan; migration forward-only dan kolom baru kompatibel.

## Pengujian

| Test | Perintah | Hasil |
|---|---|---|
| Build API Release | `dotnet build backend/Parkir.Api/Parkir.Api.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Build POS WPF Release | `dotnet build pos/Parkir.Pos/Parkir.Pos.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Regression tests | `dotnet run --project tests/Parkir.Tests/Parkir.Tests.csproj --configuration Release` | **PASS** — 25/25 |
| Security headers | test WebApplicationFactory | **PASS** — CSP, nosniff, frame deny terkirim |
| Secret scan | `bash tools/scan-secrets.sh` | **PASS** |
| Device credential integration | belum dijalankan | **NOT RUN** — database PostgreSQL tidak tersedia |
| TLS end-to-end | belum dijalankan | **BLOCKED** — sertifikat/reverse proxy belum tersedia |
| Firmware build | `pio run` | **BLOCKED** — PlatformIO tidak terpasang |

## Acceptance criteria Gelombang 2

| Kriteria | Status |
|---|---|
| Tidak ada kredensial default production | **PASS** — fallback DB dihapus |
| Port database tidak terekspos | **PASS** — port Compose dihapus |
| Kredensial unik per device | **PARTIAL** — kode dan hasher diuji; DB integration NOT RUN |
| Perangkat dicabut ditolak | **PARTIAL** — query aktif + revocation; DB integration NOT RUN |
| Device tidak dapat menyamar sebagai gate lain | **PARTIAL** — binding controller ada; DB integration NOT RUN |
| User nonaktif tidak dapat login | **PARTIAL** — query `is_active`; DB integration NOT RUN |
| Security headers/rate limit | **PASS** — middleware/konfigurasi build |
| TLS production | **BLOCKED** — konfigurasi tersedia, deployment TLS belum diuji |

## Risiko tersisa

1. Firmware/POS lama memakai API key global; setelah upgrade API, perangkat harus menerima kredensial unik dan dikonfigurasi ulang sebelum beroperasi.
2. Kredensial perangkat belum memiliki masa berlaku otomatis; rotasi/revocation manual melalui admin.
3. JWT yang sudah terbit tetap berlaku sampai expiry meskipun user dinonaktifkan. Revocation token/refresh token belum diterapkan.
4. `Security:RequireHttps` memerlukan TLS langsung atau reverse proxy dengan forwarded headers yang benar; belum diuji.
5. Database integration test dan uji perangkat nyata tetap belum dijalankan.

## Tindakan yang membutuhkan persetujuan

- Menyediakan database staging/test untuk integration test kredensial, revocation, binding, dan user disable.
- Menyediakan sertifikat/reverse proxy TLS untuk pengujian HTTPS.
- Menjadwalkan downtime/reflashing firmware dan provisioning kredensial perangkat.
- Menentukan kebijakan rotasi kredensial dan pencabutan JWT.

## Rekomendasi gelombang berikutnya

Gelombang 3 — Gate and ticket reliability: state machine entry/print/gate, ACK command, reconnect, printer state, dan reprint/cancel dengan audit.

---

# Gelombang 3 — Gate and ticket reliability

## Baseline

- Branch kerja: `fix/gate-reliability`
- Commit awal: `d158981` (`fix: harden API and device authentication`)
- Commit akhir: branch head setelah commit Gelombang 3 (lihat `git log -1 --oneline`)
- Working tree saat baseline: bersih
- Migration baru: `012_add_ticket_gate_lifecycle.sql`

## Temuan yang diverifikasi/diperbaiki

| ID | Status | Bukti ringkas |
|---|---|---|
| R-13 — status printer/tiket | **PARTIAL** | Transaksi memiliki `print_status`; firmware melaporkan `PRINT_SENT`/`PRINT_FAILED`; status fisik printer belum tersedia. |
| R-15 — command/ACK gate | **PARTIAL** | POS mengirim command gate `SENT` lalu `EXECUTED`/`FAILED` dengan command ID; tabel `gate_commands` menyimpan expiry/status/audit. Belum ada feedback posisi fisik gate. |
| R-14 — Wi-Fi reconnect | **FIXED (firmware code)** | `ensureWifi()` memakai exponential backoff dan dipanggil dari loop tanpa blocking lama. |
| Reprint/cancel | **PARTIAL** | Cancel memakai void beralasan yang sudah ada; reprint mencatat alasan dan hitungan di server, tetapi pencetakan fisik tetap lokal. |
| Idempotent retry | **PARTIAL** | Entry idempotency dan print-status idempotent; command gate memakai command ID, tetapi hardware retry dapat membuat command ID baru. |

## Perubahan yang dilakukan

| File | Ringkasan |
|---|---|
| `database/migrations/012_add_ticket_gate_lifecycle.sql` | Kolom `print_status`, `reprint_count`, `last_reprint_at`; tabel `gate_commands`. |
| `database/schema.sql` | Skema lifecycle cetak dan command gate. |
| `backend/Parkir.Api/Data/GateRepository.cs` | Transisi print-status, reprint beralasan, command gate + audit. |
| `backend/Parkir.Api/Services/TicketLifecycle.cs` | Aturan transisi status cetak dan command gate yang dapat diuji. |
| `backend/Parkir.Api/Services/ParkingService.cs` | Validasi dan orkestrasi print-status/reprint/gate-event. |
| `backend/Parkir.Api/Controllers/ParkingController.cs` | Endpoint print-status, reprint, dan gate-event. |
| `firmware/gate_in/gate_in.ino` | Reconnect Wi-Fi backoff dan laporan status cetak. |
| `pos/Parkir.Pos/MainWindow.xaml.cs` | Laporan command gate `SENT`/`EXECUTED`/`FAILED` dengan timeout 1 detik. |
| `tests/Parkir.Tests/Program.cs` | Test transisi lifecycle; jumlah migration 11. |
| `README.md` | Endpoint, status cetak, gate event, reconnect, dan migration 012. |

## Database

- Migration: `012_add_ticket_gate_lifecycle.sql`.
- Dampak data: kolom baru nullable/default dan tabel command baru; tidak menghapus data.
- Cara menjalankan: otomatis oleh `DatabaseInitializer`; backup sebelum deploy.
- Rollback: kolom/tabel baru kompatibel dengan aplikasi lama; migration forward-only.

## Pengujian

| Test | Perintah | Hasil |
|---|---|---|
| Build API Release | `dotnet build backend/Parkir.Api/Parkir.Api.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Build POS WPF Release | `dotnet build pos/Parkir.Pos/Parkir.Pos.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Regression tests | `dotnet run --project tests/Parkir.Tests/Parkir.Tests.csproj --configuration Release` | **PASS** — 26/26 |
| Secret scan | `bash tools/scan-secrets.sh` | **PASS** |
| Integration PostgreSQL | belum dijalankan | **NOT RUN** — database tidak tersedia |
| Firmware build/reconnect | `pio run` | **BLOCKED** — PlatformIO tidak terpasang |
| Printer/gate hardware | belum dijalankan | **BLOCKED** — perangkat fisik tidak tersedia |

## Acceptance criteria Gelombang 3

| Kriteria | Status |
|---|---|
| Printer failure tidak membuat tiket ganda | **PARTIAL** — idempotency entry + print status; DB/hardware test belum |
| Sistem membedakan data dikirim dan bukti cetak | **PASS konsep** — `PRINT_SENT` dipisahkan dari `PRINT_CONFIRMED`; firmware hanya mengirim SENT/FAILED |
| Command gate memiliki ID/status/audit | **PARTIAL** — implementasi ada; gate feedback fisik belum |
| Command kedaluwarsa | **PARTIAL** — `expires_at` disimpan; worker expiry belum ada |
| Reconnect tidak menduplikasi operasi | **PARTIAL** — firmware reconnect code; hardware test belum |
| Reprint/cancel tercatat | **PARTIAL** — server audit tersedia; reprint fisik lokal belum diuji |
| Regression test lulus | **PASS** — 26/26 |

## Risiko tersisa

1. `PRINT_SENT` bukan bukti kertas tercetak; printer tanpa status fisik tetap membutuhkan prosedur operator.
2. Gate command bersifat audit/best-effort dan dikirim setelah pembayaran; kegagalan API tidak menahan relay lokal.
3. Expiry command hanya disimpan, belum ada proses yang menandai command kedaluwarsa atau memicu alarm.
4. Hardware retry dapat memakai command ID baru; idempotensi gate belum sempurna.
5. Integration test PostgreSQL dan uji printer/gate fisik belum dilakukan.
6. Reprint endpoint mencatat permintaan, bukan memicu printer gate secara otomatis.

## Tindakan yang membutuhkan persetujuan

- Menyediakan PostgreSQL test untuk menguji migration 012 dan transisi command.
- Menyediakan printer/gate bench untuk uji nyata status cetak dan feedback.
- Menentukan apakah command gate harus dikirim melalui controller jaringan (Opsi B) atau tetap lokal di POS.
- Menentukan kebijakan expiry/retry command dan reprint fisik.

## Rekomendasi gelombang berikutnya

Gelombang 4 — POS dan offline reliability: rekonsiliasi queue, status manual review, perlindungan SQLite, dan penanganan scanner/printer/serial yang lebih lengkap.

---

# Gelombang 4 — POS dan offline reliability

## Baseline

- Branch kerja: `fix/pos-offline-reliability`
- Commit awal: `9b09a14` (`fix: add ticket print and gate lifecycle tracking`)
- Commit akhir: branch head setelah commit Gelombang 4 (lihat `git log -1 --oneline`)
- Working tree saat baseline: bersih
- Migration baru: tidak ada

## Temuan yang diverifikasi/diperbaiki

| ID | Status | Bukti ringkas |
|---|---|---|
| R-17 — offline queue | **PARTIAL** | Outbox memakai WAL, busy timeout, retry/backoff, dead-letter, log lokal, status server, dan tombol tindakan. Quote/pembayaran final tetap wajib server. |
| R-18 — data SQLite | **PARTIAL** | `outbox.log`, mode WAL, busy timeout, dan ACL file best-effort di Windows. DPAPI/enkripsi penuh belum diterapkan. |
| Peripheral handling | **PARTIAL** | Printer/relay error sudah dilaporkan; scanner tetap HID; belum ada UI pemilihan ulang port/perangkat. |
| Offline boundary | **FIXED dokumentasi + UI** | POS menampilkan status server offline dan tidak mengizinkan quote baru tanpa server. Checkout yang sudah memiliki quote tetap masuk outbox dan menunggu konfirmasi. |
| Dead-letter recovery | **PARTIAL** | Daftar item gagal ditampilkan, retry semua tersedia, log tersimpan. Penghapusan/acknowledge per item dan rekonsiliasi finansial penuh belum ada. |

## Perubahan yang dilakukan

| File | Ringkasan |
|---|---|
| `pos/Parkir.Pos/CheckoutQueue.cs` | WAL, busy timeout, log outbox, ACL Windows best-effort, operasi queue tetap idempotent. |
| `pos/Parkir.Pos/MainWindow.xaml.cs` | Status server offline/null/online, detail item gagal, retry all, pesan antrean. |
| `README.md` | Batas offline, outbox log/ACL, dan kebijakan quote wajib server. |
| `REMEDIATION_STATUS.md` | Status Gelombang 4. |

## Database

- Migration: tidak ada.
- Dampak data: tidak ada perubahan skema.
- Cara menjalankan: tidak applicable.
- Rollback: tidak applicable.

## Pengujian

| Test | Perintah | Hasil |
|---|---|---|
| Build API Release | `dotnet build backend/Parkir.Api/Parkir.Api.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Build POS WPF Release | `dotnet build pos/Parkir.Pos/Parkir.Pos.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Regression tests | `dotnet run --project tests/Parkir.Tests/Parkir.Tests.csproj --configuration Release` | **PASS** — 26/26 |
| Queue SQLite transition/migration | test runner | **PASS** — PENDING/CONFIRMED/HARDWARE_DONE/FAILED/retry |
| Secret scan | `bash tools/scan-secrets.sh` | **PASS** |
| Offline/Windows WPF | belum dijalankan | **NOT RUN** — environment Linux |
| PostgreSQL integration | belum dijalankan | **NOT RUN** — database tidak tersedia |
| Printer/relay fisik | belum dijalankan | **BLOCKED** — hardware tidak tersedia |

## Acceptance criteria Gelombang 4

| Kriteria | Status |
|---|---|
| Item queue tidak hilang diam-diam | **PARTIAL** — status/log/dead-letter ada; test Windows crash belum |
| Retry tidak menggandakan checkout | **PASS** — idempotency key persisten; test queue lulus |
| Pembayaran final offline | **PASS** — tidak diizinkan; quote wajib server |
| Perangkat peripheral gagal memiliki status | **PARTIAL** — printer/relay status ada; pemulihan UI terbatas |
| File SQLite dilindungi | **PARTIAL** — ACL Windows best-effort; DPAPI tidak |
| Queue recovery dapat ditinjau | **PARTIAL** — retry all dan detail; ack/delete per item belum |

## Risiko tersisa

1. Belum ada uji WPF pada Windows; ACL, WAL, dan perilaku saat crash belum diverifikasi di runtime nyata.
2. `outbox.log` belum memiliki rotasi/retensi.
3. Item dead-letter hanya bisa di-retry semua; belum ada acknowledge/delete per item dan rekonsiliasi finansial formal.
4. Scanner dan pemilihan ulang port serial/printer masih manual.
5. SQLite tidak dienkripsi penuh; hanya ACL file best-effort.
6. Integration PostgreSQL dan hardware tetap belum dijalankan.

## Tindakan yang membutuhkan persetujuan

- Menyediakan mesin Windows untuk uji WPF, ACL, dan crash recovery.
- Menentukan kebijakan retensi `outbox.log` dan dead-letter.
- Menentukan apakah data outbox harus dienkripsi DPAPI/SQLCipher.
- Menyediakan printer/relay bench untuk uji peripheral.

## Rekomendasi gelombang berikutnya

Gelombang 5 — Membership, tarif, timezone, dan reporting: anti-passback non-member, validasi membership, kebijakan tarif, daily cutover, dan rekonsiliasi laporan.

---

# Gelombang 5 — Membership, tarif, timezone, dan reporting

## Baseline

- Branch kerja: `fix/membership-timezone-reporting`
- Commit awal: `3bc8691` (`fix: improve POS offline queue reliability`)
- Commit akhir: branch head setelah commit Gelombang 5 (lihat `git log -1 --oneline`)
- Working tree saat baseline: bersih
- Migration baru: `013_add_member_entry_snapshot.sql`

## Temuan yang diverifikasi/diperbaiki

| ID | Status | Bukti ringkas |
|---|---|---|
| R-08 — anti-passback | **PARTIAL** | Member RFID aktif dicek saat entry; non-member belum punya identitas plat pada alur entry. |
| Membership expiry | **FIXED (policy)** | Status valid saat entry disimpan di `member_entry_valid`; benefit member tidak berubah karena expiry/status di tengah sesi. |
| Renewal member | **PARTIAL** | Endpoint `PUT /api/admin/members/{id}` memperbarui nama/plat/kelas/expiry dengan audit; UI dashboard belum memakai endpoint ini. |
| R-19 — timezone | **PARTIAL** | `Business:TimeZone` dipakai untuk laporan/dashboard/tanggal; lokasi operasional final belum ditetapkan. |
| Tarif | **FIXED dokumentasi** | Formula grace/rounding/max daily/lost ticket didokumentasikan; snapshot tarif sudah ada. |
| Reporting reconciliation | **PARTIAL** | Laporan API/CSV menambahkan Paid/Void/Unpaid; dashboard lama tetap kompatibel tetapi belum menampilkan kolom baru. |

## Perubahan yang dilakukan

| File | Ringkasan |
|---|---|
| `database/migrations/013_add_member_entry_snapshot.sql` | Kolom `member_entry_valid` nullable untuk snapshot benefit member. |
| `database/schema.sql` | Kolom snapshot membership. |
| `backend/Parkir.Api/Data/ParkingRepository.cs` | Set snapshot saat entry; quote memakai snapshot dengan fallback data lama. |
| `backend/Parkir.Api/Data/AdminRepository.cs` | Update member/renewal + audit atomic; laporan menambahkan hitungan Paid/Void/Unpaid. |
| `backend/Parkir.Api/Services/AdminService.cs` | Validasi renewal dan total report baru. |
| `backend/Parkir.Api/Controllers/AdminController.cs` | Endpoint update member dan CSV reconciliation. |
| `backend/Parkir.Api/Models/ParkingDtos.cs` | DTO membership update dan kolom report. |
| `README.md` | Kebijakan membership, formula tarif, anti-passback, report, migration 013. |
| `tests/Parkir.Tests/Program.cs` | Jumlah migration 12; test sebelumnya tetap lulus. |

## Database

- Migration: `013_add_member_entry_snapshot.sql`.
- Dampak data: kolom nullable; ticket lama memakai fallback status member saat ini.
- Cara menjalankan: otomatis oleh `DatabaseInitializer`; backup sebelum deploy.
- Rollback: kolom nullable; migration forward-only.

## Pengujian

| Test | Perintah | Hasil |
|---|---|---|
| Build API Release | `dotnet build backend/Parkir.Api/Parkir.Api.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Build POS WPF Release | `dotnet build pos/Parkir.Pos/Parkir.Pos.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Regression tests | `dotnet run --project tests/Parkir.Tests/Parkir.Tests.csproj --configuration Release` | **PASS** — 26/26 |
| Secret scan | `bash tools/scan-secrets.sh` | **PASS** |
| Integration PostgreSQL | belum dijalankan | **NOT RUN** — database tidak tersedia |
| Timezone/report DB | belum dijalankan | **NOT RUN** — butuh data uji |
| Member renewal UI | belum dijalankan | **NOT RUN** — endpoint belum dipakai dashboard |

## Acceptance criteria Gelombang 5

| Kriteria | Status |
|---|---|
| Membership valid saat entry | **PASS** — snapshot disimpan |
| Expiry/status di tengah sesi tidak mengubah tarif | **PASS** — policy snapshot |
| Renewal member beralasan/audit | **PARTIAL** — endpoint ada; UI/test DB belum |
| Formula tarif eksplisit | **PASS dokumentasi** |
| Timezone konsisten | **PARTIAL** — config dan test batas hari; lokasi final belum |
| Report dapat direkonsiliasi | **PARTIAL** — field status tersedia; dashboard belum menampilkan |
| Regression test | **PASS** — 26/26 |

## Risiko tersisa

1. Kebijakan snapshot member berarti penangguhan/expiry setelah entry tidak mengubah tarif sesi; ini keputusan bisnis yang perlu disetujui.
2. Non-member tetap belum memiliki anti-passback karena entry tidak mengirim plat/RFID.
3. Kolom report baru belum ditampilkan di dashboard; hanya API/CSV.
4. `UnpaidEntries` adalah kondisi saat laporan diambil, sehingga laporan historis dapat berubah saat tiket terbuka ditutup.
5. Timezone lokasi operasional final belum ditentukan; default `Asia/Jakarta` harus dikonfirmasi.
6. Migration/report/membership belum diuji pada PostgreSQL nyata.

## Tindakan yang membutuhkan persetujuan

- Menyetujui kebijakan benefit member saat entry vs exit.
- Menentukan timezone lokasi operasional.
- Menentukan apakah dashboard perlu menampilkan kolom Paid/Void/Unpaid.
- Menyediakan database test untuk membership/renewal/report.

## Rekomendasi gelombang berikutnya

Gelombang 6 — QA, CI, monitoring, backup: integration/E2E tests, dependency/secret scan, structured logging, health alert, dan restore drill.

---

# Gelombang 6 — QA, CI, monitoring, dan backup

## Baseline

- Branch kerja: `test/production-readiness`
- Commit awal: `71c9406` (`fix: formalize membership and report reconciliation`)
- Commit akhir: branch head setelah commit Gelombang 6 (lihat `git log -1 --oneline`)
- Working tree saat baseline: bersih
- Migration baru: tidak ada (workflow/smoke test memakai migration yang ada)

## Perubahan yang dilakukan

| File | Ringkasan |
|---|---|
| `.github/workflows/database.yml` | CI PostgreSQL 16: apply `schema.sql`, semua migration dua kali, lalu smoke check invariant. |
| `database/smoke.sql` | Assertion tabel/index/kolom penting (`device_credentials`, `gate_commands`, anti-passback, snapshot member, print status). |
| `.github/workflows/tests.yml` | Menambahkan dependency vulnerability scan untuk API dan POS. |
| `tools/backup-postgres.sh` | Backup custom-format + SHA-256 + retensi. |
| `tools/restore-postgres.sh` | Restore dengan konfirmasi eksplisit dan `--clean --if-exists`. |
| `docs/OPERATIONS.md` | Runbook deploy, monitoring, backup, restore drill, POS offline, gate/printer, dan recovery. |
| `README.md` | Menautkan runbook serta menjelaskan CI/smoke test. |
| `.gitignore` | Mengabaikan dump backup dan direktori backup. |

## Database

- Migration: tidak ada perubahan skema.
- Dampak data: tidak ada.
- Cara menjalankan smoke: workflow `Database smoke` pada PR/push.
- Rollback: tidak applicable.

## Pengujian

| Test | Perintah | Hasil |
|---|---|---|
| Build API Release | `dotnet build backend/Parkir.Api/Parkir.Api.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Build POS WPF Release | `dotnet build pos/Parkir.Pos/Parkir.Pos.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Regression tests | `dotnet run --project tests/Parkir.Tests/Parkir.Tests.csproj --configuration Release` | **PASS** — 26/26 |
| Secret scan | `bash tools/scan-secrets.sh` | **PASS** |
| Database smoke PostgreSQL | workflow `database.yml` | **NOT RUN lokal** — server database tidak tersedia; workflow siap dijalankan CI |
| Dependency vulnerability scan | workflow `tests.yml` | **NOT RUN lokal** — membutuhkan NuGet/CI |
| Backup/restore drill | scripts + `docs/OPERATIONS.md` | **NOT RUN** — membutuhkan database dan storage terpisah |
| WPF runtime | belum dijalankan | **NOT RUN** — environment Linux |

## Acceptance criteria Gelombang 6

| Kriteria | Status |
|---|---|
| CI build + regression test | **PASS lokal**; workflow CI tersedia |
| CI migration/schema smoke | **READY**, belum dijalankan pada CI nyata |
| Dependency/secret scan | **PARTIAL** — secret scan lokal PASS; dependency scan belum dijalankan |
| Structured logging/correlation | **PASS** — correlation ID dan health check sudah ada |
| Backup dan restore documented | **PASS dokumentasi**; restore drill belum |
| Monitoring perangkat/queue | **PARTIAL** — status heartbeat/queue ada; alert eksternal belum |
| Runbook operasional | **PASS dokumentasi**; perlu validasi operator |

## Risiko tersisa

1. Workflow `Database smoke` dan backup/restore belum dieksekusi pada lingkungan nyata.
2. Belum ada sistem alert eksternal (email/Slack/PagerDuty); monitoring masih health/status manual.
3. Backup perlu disimpan terenkripsi di lokasi terpisah dan diuji restore.
4. WPF dan perangkat fisik tetap belum diuji.
5. Dependency scan CI dapat berubah mengikuti database advisory NuGet.

## Tindakan yang membutuhkan persetujuan

- Menjalankan workflow CI/staging dan restore drill.
- Menentukan platform alert dan retensi log/backup.
- Menyediakan akun/storage backup terpisah.
- Menjadwalkan uji WPF dan hardware pada Gelombang 7.

## Rekomendasi gelombang berikutnya

Gelombang 7 — Integrasi perangkat dan pilot readiness: wiring/device mapping, Gate-In/Gate-Out bench test, command/feedback, hardware-in-the-loop, checklist keselamatan, dan pilot checklist.

---

# Gelombang 7 — Integrasi perangkat dan pilot readiness

## Baseline

- Branch kerja: `test/hardware-pilot`
- Commit awal: `bbdff0d` (`test: add CI database smoke and operations runbook`)
- Commit akhir: branch head setelah commit Gelombang 7 (lihat `git log -1 --oneline`)
- Working tree saat baseline: bersih
- Migration baru: tidak ada

## Perubahan yang dilakukan

| File | Ringkasan |
|---|---|
| `docs/adr/0001-gate-out-architecture.md` | ADR Opsi A/B; status keputusan masih menunggu pemilik sistem. |
| `docs/DEVICE_MAPPING.md` | Template pemetaan Gate ID/Device ID, firmware, IP, status kredensial; tanpa secret. |
| `docs/HARDWARE_TEST_PLAN.md` | Skenario Gate-In, Gate-Out, printer, reconnect, command, dan keselamatan; semua status awal NOT RUN. |
| `docs/PILOT_READINESS.md` | Checklist keamanan, transaksi, gate, operasional, go/no-go, dan larangan production approval oleh agent. |
| `hardware/gate_out/wiring.md` | Template wiring/bench Gate-Out Opsi A dan peringatan keselamatan. |
| `README.md` | Tautan dokumentasi perangkat/pilot. |

## Pengujian

| Test | Perintah | Hasil |
|---|---|---|
| Build API Release | `dotnet build backend/Parkir.Api/Parkir.Api.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Build POS WPF Release | `dotnet build pos/Parkir.Pos/Parkir.Pos.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Regression tests | `dotnet run --project tests/Parkir.Tests/Parkir.Tests.csproj --configuration Release` | **PASS** — 26/26 |
| Secret scan | `bash tools/scan-secrets.sh` | **PASS** |
| Gate-In hardware scenarios | `docs/HARDWARE_TEST_PLAN.md` | **NOT RUN** — hardware tidak tersedia |
| Gate-Out hardware scenarios | `docs/HARDWARE_TEST_PLAN.md` | **NOT RUN** — hardware tidak tersedia |
| Emergency stop/anti-crush | uji fisik teknisi | **BLOCKED** — membutuhkan teknisi dan barrier |
| Pilot checklist | `docs/PILOT_READINESS.md` | **BLOCKED** — belum ada tanda tangan/go-no-go |

## Status akhir

- **Code complete:** sebagian besar gelombang coding selesai; beberapa item tetap PARTIAL.
- **Test verified:** unit/regression PASS; integration database dan hardware NOT RUN.
- **Staging verified:** belum.
- **Hardware verified:** belum.
- **Pilot ready:** **BELUM**.
- **Production approved:** bukan kewenangan agent.

## Risiko/keputusan terbuka

1. ADR Gate-Out (Opsi A vs B) belum diputuskan.
2. Safety hardware belum diverifikasi teknisi; pilot tidak boleh dimulai.
3. Integration PostgreSQL, CI database smoke, dan backup/restore drill belum dieksekusi nyata.
4. WPF runtime/ACL dan firmware belum diuji pada lingkungan target.
5. Beberapa fitur masih PARTIAL: anti-passback non-member, dead-letter reconciliation, expiry command gate, feedback posisi gate, dan reprint fisik.

## Rekomendasi

Lakukan bench test `docs/HARDWARE_TEST_PLAN.md`, selesaikan ADR Gate-Out, jalankan CI/restore drill di staging, lalu isi `docs/PILOT_READINESS.md` dan minta persetujuan pemilik sistem.
