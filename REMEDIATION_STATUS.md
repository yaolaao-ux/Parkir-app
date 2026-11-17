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
