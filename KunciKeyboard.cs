// Kunci Keyboard: mengunci keyboard, mouse, touchpad, dan layar sentuh.
// Layar tetap terlihat (video tetap jalan). Buka dengan mengetik kata pembuka (bawaan: buka).
// Darurat: Ctrl+Alt+Del (tidak bisa diblokir Windows) langsung membuka kunci. Begitu juga Win+L dan layar UAC:
// saat Windows pindah ke desktop aman, kunci dilepas.
//
// Pakai:  KunciKeyboard.exe [--kata rahasia] [--menit 5]
//   --kata   kata pembuka, huruf/angka 2-20 karakter (bawaan: buka)
//   --menit  buka otomatis setelah sekian menit (boleh desimal, mis. 0.5)
//
// Mode admin (sekali pasang, satu kali UAC):  KunciKeyboard.exe --pasang   (lepas lagi: --copot)
//   Tanpa mode admin, jendela yang berjalan sebagai admin (Task Manager, installer, terminal admin)
//   tidak ikut terkunci: Windows tidak meneruskan tombolnya ke hook milik proses biasa.
//   --pasang menyalin exe ke Program Files dan mendaftarkan tugas terjadwal "KunciKeyboard"
//   (hak tertinggi, tanpa pemicu). Exe biasa lalu menyerahkan penguncian ke tugas itu.
//   Sesudah build ulang, jalankan --pasang lagi supaya salinan di Program Files ikut baru.
//
// Catatan kejadian: %LOCALAPPDATA%\KunciKeyboard\catatan.txt

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Kunci Keyboard")]
[assembly: System.Reflection.AssemblyProduct("Kunci Keyboard")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.0.0")]

namespace KunciKeyboard
{
    static class Program
    {
        const int Hitung = 3;

        [STAThread]
        static int Main(string[] args)
        {
            string kata = "buka";
            double menit = 0;
            string uji = null, ikon = null;
            bool pasang = false, copot = false, dariTugas = false, tanpaAdmin = false;
            List<string> argKunci = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string nilai = i + 1 < args.Length ? args[i + 1] : null;
                if ((a == "--kata" || a == "--menit") && nilai != null) { argKunci.Add(a); argKunci.Add(nilai); i++; }
                else if (a == "--uji" && nilai != null) { uji = nilai; i++; }
                else if (a == "--ikon" && nilai != null) { ikon = nilai; i++; }
                else if (a == "--pasang") pasang = true;
                else if (a == "--copot") copot = true;
                else if (a == "--tugas") dariTugas = true;
                else if (a == "--tanpa-admin") tanpaAdmin = true;
            }
            // Dijalankan tugas terjadwal: argumen datang dari berkas yang ditulis exe pemanggil.
            if (dariTugas) argKunci = Tugas.AmbilArgumen();
            BacaArgumen(argKunci, ref kata, ref menit);

            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (ikon != null) { Gambar.SimpanIkon(ikon); return 0; }
            if (uji != null) return Uji(uji);
            if (pasang || copot) return Tugas.Atur(copot);

            if (!Regex.IsMatch(kata, "^[a-z0-9]{2,20}$"))
            {
                MessageBox.Show("Kata pembuka harus huruf atau angka, 2 sampai 20 karakter.", "Kunci Keyboard",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }
            if (menit < 0 || menit > 600)
            {
                MessageBox.Show("--menit harus antara 0 dan 600.", "Kunci Keyboard",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }

            // Proses biasa: serahkan ke tugas admin bila terpasang, supaya jendela admin ikut terkunci.
            if (!dariTugas && !tanpaAdmin && !Tugas.Admin() && Tugas.Serahkan(argKunci)) return 0;

            return Jalankan(kata, menit, dariTugas);
        }

        static void BacaArgumen(List<string> a, ref string kata, ref double menit)
        {
            for (int i = 0; i + 1 < a.Count; i++)
            {
                if (a[i] == "--kata") { kata = a[i + 1].Trim().ToLowerInvariant(); i++; }
                else if (a[i] == "--menit")
                {
                    if (!double.TryParse(a[i + 1].Trim().Replace(',', '.'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out menit)) menit = -1;
                    i++;
                }
            }
        }

        static int Jalankan(string kata, double menit, bool dariTugas)
        {
            Mutex satu;
            bool baru;
            try { satu = new Mutex(true, "Local\\KunciKeyboard.SatuSaja", out baru); }
            catch (UnauthorizedAccessException) { return 1; } // sudah jalan sebagai admin

            using (satu)
            {
                if (!baru) { if (dariTugas) Tugas.BeriSinyal(); return 1; }

                try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }

                // Galat tampilan tidak boleh membuka kunci: hook hidup di thread sendiri dan
                // kata pembuka tetap berfungsi walau lencana rusak.
                int galatUi = 0;
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    if (galatUi++ < 5) Catatan.Tulis("galat tampilan: " + e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Catatan.Tulis("galat fatal: " + e.ExceptionObject);
                    Pengunci.Darurat();
                };

                Catatan.Tulis("mulai" + (menit > 0 ? ", buka otomatis " + menit.ToString(CultureInfo.InvariantCulture) + " menit" : "")
                    + (dariTugas ? ", lewat tugas admin" : ""));

                Pengunci p = new Pengunci(kata, menit, Hitung, Skala());
                if (!p.Siap)
                {
                    Catatan.Tulis("gagal memasang hook");
                    MessageBox.Show("Gagal memasang pengunci input.", "Kunci Keyboard",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 3;
                }
                if (dariTugas) Tugas.BeriSinyal();
                Application.Run(p);
                GC.KeepAlive(satu);
            }
            return 0;
        }

        public static float Skala()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        // Mode uji: jalankan thread penjaga (hook dipasang ulang tiap detik) tanpa memblokir apa pun,
        // lalu render semua keadaan lencana ke PNG.
        static int Uji(string folder)
        {
            Directory.CreateDirectory(folder);

            Native.HookProc lewat = delegate(int n, IntPtr w, IntPtr l) { return Native.CallNextHookEx(IntPtr.Zero, n, w, l); };
            Penjaga pj = new Penjaga(lewat, lewat, null, false);
            bool ok = pj.Mulai();
            if (ok) Thread.Sleep(3400);
            pj.Henti();
            bool rapi = pj.Tunggu(2000);
            GC.KeepAlive(lewat);
            bool biasa = Native.DesktopBiasa();
            bool lolos = ok && rapi && biasa && pj.JumlahGagal == 0 && pj.JumlahPasang >= 4 && pj.IdThread != Native.GetCurrentThreadId();
            File.WriteAllText(Path.Combine(folder, "hook.txt"),
                "hook: " + (ok ? "ok" : "GAGAL") + "\r\n" +
                "thread hook: " + pj.IdThread + ", thread UI: " + Native.GetCurrentThreadId() + "\r\n" +
                "pasang (awal + ulang): " + pj.JumlahPasang + ", gagal: " + pj.JumlahGagal + "\r\n" +
                "berhenti rapi: " + (rapi ? "ya" : "TIDAK") + "\r\n" +
                "admin: " + (Tugas.Admin() ? "ya" : "tidak") + "\r\n" +
                "desktop input biasa: " + (biasa ? "ya" : "TIDAK") + "\r\n" +
                "hasil: " + (lolos ? "LOLOS" : "GAGAL") + "\r\n");

            Isi[] daftar = { Isi.Hitung(3), Isi.Terkunci("buka", -1), Isi.Terkunci("buka", 299), Isi.Terbuka() };
            float s = 2f;
            List<Bitmap> gambar = new List<Bitmap>();
            int lebar = 0, tinggi = 0;
            foreach (Isi isi in daftar)
            {
                Bitmap b = Gambar.Lencana(isi, s);
                gambar.Add(b);
                lebar = Math.Max(lebar, b.Width);
                tinggi += b.Height;
            }
            int pad = 40;
            using (Bitmap lembar = new Bitmap(lebar * 2 + pad * 3, tinggi + pad * 2))
            using (Graphics g = Graphics.FromImage(lembar))
            {
                int separuh = lembar.Width / 2;
                using (LinearGradientBrush gelap = new LinearGradientBrush(new Rectangle(0, 0, separuh, lembar.Height),
                    Color.FromArgb(15, 23, 42), Color.FromArgb(71, 85, 105), 60f))
                    g.FillRectangle(gelap, 0, 0, separuh, lembar.Height);
                using (LinearGradientBrush terang = new LinearGradientBrush(new Rectangle(separuh, 0, separuh, lembar.Height),
                    Color.FromArgb(248, 250, 252), Color.FromArgb(203, 213, 225), 60f))
                    g.FillRectangle(terang, separuh, 0, lembar.Width - separuh, lembar.Height);
                int y = pad;
                foreach (Bitmap b in gambar)
                {
                    g.DrawImage(b, pad, y, b.Width, b.Height);
                    g.DrawImage(b, separuh + pad / 2, y, b.Width, b.Height);
                    y += b.Height;
                }
                lembar.Save(Path.Combine(folder, "lencana.png"), ImageFormat.Png);
            }
            foreach (Bitmap b in gambar) b.Dispose();

            using (Bitmap ik = Gambar.Ikon(256)) ik.Save(Path.Combine(folder, "ikon-256.png"), ImageFormat.Png);
            using (Bitmap ik = Gambar.Ikon(32)) ik.Save(Path.Combine(folder, "ikon-32.png"), ImageFormat.Png);
            return lolos ? 0 : 4;
        }
    }

    // ------------------------------------------------------------------ inti

#pragma warning disable 420 // volatile + Interlocked: aman, Interlocked sendiri sudah memagari memori

    class Pengunci : ApplicationContext
    {
        public const int HITUNG = 0, TERKUNCI = 1, TERBUKA = 2, BATAL = 3;
        static Pengunci aktif;

        readonly string kata;
        readonly double menit;
        readonly int hitung;
        readonly Tirai tirai;
        readonly Lencana lencana;
        readonly System.Windows.Forms.Timer detak;
        readonly Native.HookProc procKb, procMs;
        readonly Penjaga penjaga;

        // Dibaca bersama thread hook dan thread UI.
        volatile int keadaan = HITUNG;
        public const int SEBAB_KATA = 0, SEBAB_WAKTU = 1, SEBAB_LAYAR_AMAN = 2;
        volatile bool mintaTampil;
        volatile int sebabBuka;
        volatile HashSet<uint> tertahan = new HashSet<uint>();
        long batasTik; // DateTime.UtcNow.Ticks saat buka otomatis, 0 = tanpa batas

        // Milik thread hook selama terkunci.
        string ketikan = "";

        // Milik thread UI.
        bool uiSelesai;
        DateTime mulai, sentuhTerakhir, cekTerakhir;
        int angkaTampil = int.MinValue;
        IntPtr fgDicatat;

        public bool Siap { get; private set; }

        public Pengunci(string kata, double menit, int hitung, float skala)
        {
            aktif = this;
            this.kata = kata;
            this.menit = menit;
            this.hitung = hitung;

            tirai = new Tirai(this);
            lencana = new Lencana(skala);
            lencana.Show();

            procKb = KbProc;
            procMs = MsProc;
            penjaga = new Penjaga(procKb, procMs, TikPenjaga, true);
            Siap = penjaga.Mulai();
            if (!Siap) return;

            mulai = DateTime.UtcNow;
            angkaTampil = hitung;
            lencana.Atur(Isi.Hitung(hitung));
            lencana.Tampil();

            detak = new System.Windows.Forms.Timer();
            detak.Interval = 50;
            detak.Tick += Detak;
            detak.Start();
        }

        public static void Darurat()
        {
            try { if (aktif != null) aktif.keadaan = BATAL; } catch { }
            Environment.Exit(1); // Windows mencabut semua hook milik proses yang berakhir
        }

        public void Sentuh() { mintaTampil = true; }

        // Dipanggil thread hook atau thread UI.
        void Buka(int sebab)
        {
            sebabBuka = sebab;
            if (Interlocked.CompareExchange(ref keadaan, TERBUKA, TERKUNCI) == TERKUNCI) penjaga.Henti();
        }

        // Thread hook, tiap 250 ms: tetap jalan walau thread UI macet.
        void TikPenjaga()
        {
            if (keadaan != TERKUNCI) return;
            long batas = Interlocked.Read(ref batasTik);
            if (batas > 0 && DateTime.UtcNow.Ticks >= batas) { Buka(SEBAB_WAKTU); return; }
            // Ctrl+Alt+Del, Win+L, atau UAC: Windows pindah ke desktop aman. Di mode admin hook ini
            // ikut memblokir Task Manager, jadi inilah jalan darurat: kunci dilepas.
            if (!Native.DesktopBiasa()) Buka(SEBAB_LAYAR_AMAN);
        }

        void Detak(object sender, EventArgs e)
        {
            if (uiSelesai) return;
            DateTime kini = DateTime.UtcNow;
            int k = keadaan;

            if (k == BATAL) { Catatan.Tulis("dibatalkan"); Selesai(null); return; }
            if (k == TERBUKA)
            {
                int sb = sebabBuka;
                Catatan.Tulis(sb == SEBAB_WAKTU ? "terbuka: waktu habis"
                    : sb == SEBAB_LAYAR_AMAN ? "terbuka: Ctrl+Alt+Del / layar kunci / UAC" : "terbuka: kata pembuka");
                Selesai(Isi.Terbuka());
                return;
            }
            if (k == HITUNG)
            {
                int sisa = hitung - (int)Math.Floor((kini - mulai).TotalSeconds);
                if (sisa <= 0) { Kunci(kini); return; }
                if (sisa != angkaTampil) { angkaTampil = sisa; lencana.Atur(Isi.Hitung(sisa)); }
                return;
            }

            long batas = Interlocked.Read(ref batasTik);
            if (batas > 0)
            {
                if (kini.Ticks >= batas) { Buka(SEBAB_WAKTU); return; }
                int det = (int)Math.Ceiling(TimeSpan.FromTicks(batas - kini.Ticks).TotalSeconds);
                if (det != angkaTampil) { angkaTampil = det; lencana.Atur(Isi.Terkunci(kata, det)); }
            }

            if (mintaTampil)
            {
                mintaTampil = false;
                sentuhTerakhir = kini;
                lencana.Tampil();
            }
            else if ((kini - sentuhTerakhir).TotalSeconds > 3.5)
            {
                lencana.Pudar();
            }

            if ((kini - cekTerakhir).TotalMilliseconds >= 500)
            {
                cekTerakhir = kini;
                JagaDiAtas();
            }
        }

        void Kunci(DateTime kini)
        {
            HashSet<uint> t = new HashSet<uint>();
            for (int vk = 1; vk < 256; vk++)
                if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) t.Add((uint)vk);
            tertahan = t;
            ketikan = "";
            mintaTampil = false;
            Interlocked.Exchange(ref batasTik, menit > 0 ? kini.AddSeconds(menit * 60).Ticks : 0);

            // Mulai detik ini thread hook menelan semua input.
            if (Interlocked.CompareExchange(ref keadaan, TERKUNCI, HITUNG) != HITUNG) return; // Esc barusan

            tirai.Show();
            JagaDiAtas();

            angkaTampil = menit > 0 ? (int)Math.Ceiling(menit * 60) : -1;
            lencana.Atur(Isi.Terkunci(kata, angkaTampil));
            sentuhTerakhir = kini;
            lencana.Tampil();
            Catatan.Tulis("terkunci");
        }

        void Selesai(Isi akhir)
        {
            uiSelesai = true;
            penjaga.Henti();
            tirai.BolehTutup = true;
            tirai.Hide();

            int sisaLangkah = akhir != null ? 18 : 6;
            if (akhir != null) { lencana.Atur(akhir); lencana.Tampil(); }
            else lencana.Pudar();

            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 50;
            t.Tick += delegate
            {
                sisaLangkah--;
                if (sisaLangkah == 6) lencana.Pudar();
                if (sisaLangkah <= 0) { t.Stop(); ExitThread(); }
            };
            t.Start();
        }

        void JagaDiAtas()
        {
            Rectangle layar = SystemInformation.VirtualScreen;
            if (tirai.Bounds != layar) tirai.Bounds = layar;

            const uint f = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
            Native.SetWindowPos(tirai.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, f);
            Native.SetWindowPos(lencana.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, f);

            // Fokus di tirai: kalau jendela admin yang aktif, hook proses biasa tidak menerima tombolnya.
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == tirai.Handle) return;
            uint kami = Native.GetCurrentThreadId();
            uint dia = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, IntPtr.Zero);
            if (dia != 0 && dia != kami)
            {
                Native.AttachThreadInput(kami, dia, true);
                Native.SetForegroundWindow(tirai.Handle);
                Native.BringWindowToTop(tirai.Handle);
                Native.AttachThreadInput(kami, dia, false);
            }
            else Native.SetForegroundWindow(tirai.Handle);

            IntPtr sesudah = Native.GetForegroundWindow();
            if (sesudah != tirai.Handle && sesudah != IntPtr.Zero && sesudah != fgDicatat)
            {
                fgDicatat = sesudah;
                Catatan.Tulis("fokus tidak bisa direbut dari " + NamaProses(sesudah));
            }
        }

        static string NamaProses(IntPtr hwnd)
        {
            try
            {
                uint pid;
                Native.GetWindowProcessId(hwnd, out pid);
                using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName + " (pid " + pid + ")";
            }
            catch { return "jendela tak dikenal"; }
        }

        protected override void ExitThreadCore()
        {
            penjaga.UiKeluar();
            penjaga.Henti();
            if (detak != null) detak.Stop();
            tirai.BolehTutup = true;
            tirai.Close();
            lencana.Close();
            base.ExitThreadCore();
        }

        // ---- hook: dipanggil di thread penjaga, harus cepat, tanpa I/O

        IntPtr KbProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            int k = keadaan;
            try
            {
                if (nCode >= 0)
                {
                    uint vk = (uint)Marshal.ReadInt32(lParam);
                    int pesan = wParam.ToInt32();
                    bool turun = pesan == Native.WM_KEYDOWN || pesan == Native.WM_SYSKEYDOWN;

                    if (k == HITUNG)
                    {
                        if (turun && vk == 0x1B)
                        {
                            Interlocked.CompareExchange(ref keadaan, BATAL, HITUNG);
                            return (IntPtr)1;
                        }
                    }
                    else if (k == TERKUNCI)
                    {
                        if (!turun)
                        {
                            // Tombol yang sudah ditekan sebelum terkunci boleh dilepas, supaya tidak "nyangkut".
                            if (tertahan.Remove(vk)) return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                            return (IntPtr)1;
                        }
                        Catat(vk);
                        mintaTampil = true;
                        return (IntPtr)1;
                    }
                }
            }
            catch
            {
                if (k == TERKUNCI) return (IntPtr)1; // gagal tertutup, bukan gagal terbuka
            }
            return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        IntPtr MsProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            int k = keadaan;
            try
            {
                if (nCode >= 0 && k == TERKUNCI)
                {
                    int pesan = wParam.ToInt32();
                    uint lepas = 0;
                    if (pesan == Native.WM_LBUTTONUP) lepas = 0x01;
                    else if (pesan == Native.WM_RBUTTONUP) lepas = 0x02;
                    else if (pesan == Native.WM_MBUTTONUP) lepas = 0x04;
                    else if (pesan == Native.WM_XBUTTONUP)
                        lepas = ((Marshal.ReadInt32(lParam, 8) >> 16) & 0xFFFF) == 1 ? 0x05u : 0x06u;

                    if (lepas != 0 && tertahan.Remove(lepas))
                        return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                    mintaTampil = true;
                    return (IntPtr)1;
                }
            }
            catch
            {
                if (k == TERKUNCI) return (IntPtr)1;
            }
            return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        void Catat(uint vk)
        {
            char c;
            if (vk >= 0x41 && vk <= 0x5A) c = (char)('a' + (vk - 0x41));
            else if (vk >= 0x30 && vk <= 0x39) c = (char)('0' + (vk - 0x30));
            else if (vk >= 0x60 && vk <= 0x69) c = (char)('0' + (vk - 0x60));
            else return;

            ketikan += c;
            if (ketikan.Length > kata.Length) ketikan = ketikan.Substring(ketikan.Length - kata.Length);
            if (ketikan == kata) Buka(SEBAB_KATA);
        }
    }

    // Thread khusus pemegang hook. Tidak menggambar apa pun, jadi selalu sempat menjawab Windows.
    // (Hook yang telat menjawab dilewati Windows, dan bila berulang dicabut diam-diam. Dulu hook
    // numpang di thread tampilan, jadi saat lencana digambar atau fokus direbut, tombol bisa lolos.)
    // Hook juga dipasang ulang tiap detik (detak 250 ms, tiap detak cek desktop aman): bila tetap tercabut, pulih dalam satu detik, dan hook kita
    // selalu jadi yang pertama dipanggil, mendahului hook aplikasi lain (pintasan, overlay game).
    class Penjaga
    {
        readonly Native.HookProc kb, ms;
        readonly Action tik;
        readonly bool jagaKeluar;
        readonly ManualResetEvent siap = new ManualResetEvent(false);
        Thread thread;
        IntPtr hookKb, hookMs;
        volatile uint idThread;
        volatile bool berhasil, uiKeluar, mintaHenti;
        public volatile int JumlahPasang, JumlahGagal;

        public Penjaga(Native.HookProc kb, Native.HookProc ms, Action tik, bool jagaKeluar)
        {
            this.kb = kb;
            this.ms = ms;
            this.tik = tik;
            this.jagaKeluar = jagaKeluar;
        }

        public uint IdThread { get { return idThread; } }

        public bool Mulai()
        {
            thread = new Thread(Putar);
            thread.IsBackground = true;
            thread.Name = "Penjaga hook";
            thread.Priority = ThreadPriority.Highest;
            thread.Start();
            siap.WaitOne(5000);
            return berhasil;
        }

        public void Henti()
        {
            mintaHenti = true;
            uint id = idThread;
            if (id != 0) Native.PostThreadMessage(id, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        public void UiKeluar() { uiKeluar = true; }

        public bool Tunggu(int ms) { return thread == null || thread.Join(ms); }

        void Putar()
        {
            try
            {
                Native.SetThreadPriority(Native.GetCurrentThread(), Native.THREAD_PRIORITY_TIME_CRITICAL);
                Native.MSG m;
                Native.PeekMessage(out m, IntPtr.Zero, 0, 0, Native.PM_NOREMOVE); // antrean pesan harus ada dulu
                idThread = Native.GetCurrentThreadId();

                Pasang();
                berhasil = hookKb != IntPtr.Zero && hookMs != IntPtr.Zero;
                siap.Set();
                if (!berhasil || mintaHenti) return;

                UIntPtr waktu = Native.SetTimer(IntPtr.Zero, UIntPtr.Zero, 250, IntPtr.Zero);
                int detak = 0;
                while (Native.GetMessage(out m, IntPtr.Zero, 0, 0) > 0)
                {
                    if (m.message != Native.WM_TIMER) continue;
                    if (++detak % 4 == 0) Pasang();
                    if (tik != null) { try { tik(); } catch { } }
                }
                Native.KillTimer(IntPtr.Zero, waktu);
            }
            catch (Exception e) { Catatan.Tulis("galat penjaga: " + e); }
            finally
            {
                Lepas();
                siap.Set();
            }

            // Kunci sudah lepas. Bila thread UI macet dan tidak menutup aplikasi, tirai bisa tetap
            // menelan klik; akhiri paksa supaya laptop tidak tersandera.
            if (berhasil && jagaKeluar)
            {
                for (int i = 0; i < 50 && !uiKeluar; i++) Thread.Sleep(100);
                if (!uiKeluar)
                {
                    Catatan.Tulis("tampilan tidak menutup, aplikasi diakhiri paksa");
                    Environment.Exit(0);
                }
            }
        }

        void Pasang()
        {
            IntPtr mod = Native.GetModuleHandle(null);
            IntPtr kbBaru = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, kb, mod, 0);
            int galatKb = Marshal.GetLastWin32Error();
            IntPtr msBaru = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, ms, mod, 0);
            int galatMs = Marshal.GetLastWin32Error();

            // Pasang yang baru dulu, baru cabut yang lama: tidak ada celah tanpa hook.
            if (kbBaru != IntPtr.Zero) { if (hookKb != IntPtr.Zero) Native.UnhookWindowsHookEx(hookKb); hookKb = kbBaru; }
            if (msBaru != IntPtr.Zero) { if (hookMs != IntPtr.Zero) Native.UnhookWindowsHookEx(hookMs); hookMs = msBaru; }

            if (kbBaru == IntPtr.Zero || msBaru == IntPtr.Zero)
            {
                JumlahGagal++;
                if (JumlahGagal <= 5)
                    Catatan.Tulis("pasang hook gagal, kode keyboard " + (kbBaru == IntPtr.Zero ? galatKb : 0)
                        + ", mouse " + (msBaru == IntPtr.Zero ? galatMs : 0));
            }
            else JumlahPasang++;
        }

        void Lepas()
        {
            if (hookKb != IntPtr.Zero) { Native.UnhookWindowsHookEx(hookKb); hookKb = IntPtr.Zero; }
            if (hookMs != IntPtr.Zero) { Native.UnhookWindowsHookEx(hookMs); hookMs = IntPtr.Zero; }
        }
    }

#pragma warning restore 420

    // ------------------------------------------------------------------ mode admin

    // Tugas terjadwal berhak tertinggi yang menjalankan salinan exe di Program Files (hanya admin
    // yang bisa menulis ke sana, jadi tugas ini tidak bisa dibelokkan ke exe lain tanpa UAC).
    static class Tugas
    {
        const string Nama = "KunciKeyboard";
        const string NamaSinyal = "Local\\KunciKeyboard.Mulai";

        static string BerkasArgumen { get { return Path.Combine(Catatan.Folder, "argumen.txt"); } }
        static string Schtasks { get { return Path.Combine(Environment.SystemDirectory, "schtasks.exe"); } }

        public static bool Admin()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static int Jalan(string argumen)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Schtasks, argumen);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi))
            {
                if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return -1; }
                return p.ExitCode;
            }
        }

        // Dipanggil exe biasa. true = tugas admin sudah mengambil alih penguncian.
        public static bool Serahkan(List<string> argumen)
        {
            try
            {
                Directory.CreateDirectory(Catatan.Folder);
                using (EventWaitHandle sinyal = new EventWaitHandle(false, EventResetMode.ManualReset, NamaSinyal))
                {
                    File.WriteAllLines(BerkasArgumen, argumen.ToArray());
                    if (Jalan("/run /tn " + Nama) != 0) { HapusArgumen(); return false; } // belum dipasang
                    if (sinyal.WaitOne(5000)) return true;
                    Catatan.Tulis("tugas admin tidak menyahut, pakai mode biasa");
                    HapusArgumen();
                    return false;
                }
            }
            catch (Exception e)
            {
                Catatan.Tulis("gagal menyerahkan ke tugas admin: " + e.Message);
                return false;
            }
        }

        // Dipanggil salinan admin. Berkas ini bisa ditulis proses biasa, jadi isinya hanya
        // dibaca sebagai --kata/--menit (dan tetap divalidasi seperti argumen biasa).
        public static List<string> AmbilArgumen()
        {
            List<string> hasil = new List<string>();
            try
            {
                string f = BerkasArgumen;
                if (!File.Exists(f)) return hasil;
                foreach (string baris in File.ReadAllLines(f)) hasil.Add(baris.Trim().ToLowerInvariant());
                File.Delete(f);
            }
            catch { }
            return hasil;
        }

        static void HapusArgumen()
        {
            try { File.Delete(BerkasArgumen); } catch { }
        }

        public static void BeriSinyal()
        {
            try
            {
                EventWaitHandle sinyal;
                if (EventWaitHandle.TryOpenExisting(NamaSinyal, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, out sinyal))
                    using (sinyal) sinyal.Set();
            }
            catch { }
        }

        public static int Atur(bool copot)
        {
            if (!Admin())
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, copot ? "--copot" : "--pasang");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    using (Process p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
                }
                catch (Win32Exception) { return 5; } // UAC ditolak
            }

            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KunciKeyboard");
            string exe = Path.Combine(folder, "KunciKeyboard.exe");
            try
            {
                if (copot)
                {
                    Jalan("/delete /tn " + Nama + " /f");
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    Catatan.Tulis("mode admin dicopot");
                    Pesan("Mode admin dicopot.", false);
                    return 0;
                }

                Directory.CreateDirectory(folder);
                if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), exe, StringComparison.OrdinalIgnoreCase))
                    File.Copy(Application.ExecutablePath, exe, true);

                string sid = WindowsIdentity.GetCurrent().User.Value;
                string xml =
                    "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                    "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                    "  <RegistrationInfo><Description>Kunci Keyboard dengan hak admin, supaya jendela admin ikut terkunci.</Description></RegistrationInfo>\r\n" +
                    "  <Principals><Principal id=\"Author\"><UserId>" + sid + "</UserId><LogonType>InteractiveToken</LogonType>" +
                    "<RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                    "  <Settings>\r\n" +
                    "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                    "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                    "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                    "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                    "    <StartWhenAvailable>false</StartWhenAvailable>\r\n" +
                    "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
                    "    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\r\n" +
                    "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                    "    <Enabled>true</Enabled>\r\n" +
                    "    <Hidden>false</Hidden>\r\n" +
                    "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
                    "    <WakeToRun>false</WakeToRun>\r\n" +
                    "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                    "    <Priority>4</Priority>\r\n" +
                    "  </Settings>\r\n" +
                    "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(exe) + "</Command>" +
                    "<Arguments>--tugas</Arguments><WorkingDirectory>" + SecurityElement.Escape(folder) + "</WorkingDirectory></Exec></Actions>\r\n" +
                    "</Task>\r\n";
                string tmp = Path.Combine(Path.GetTempPath(), "KunciKeyboard-tugas.xml");
                File.WriteAllText(tmp, xml, Encoding.Unicode);
                int kode = Jalan("/create /tn " + Nama + " /xml \"" + tmp + "\" /f");
                try { File.Delete(tmp); } catch { }
                if (kode != 0) { Pesan("Gagal mendaftarkan tugas terjadwal (kode " + kode + ").", true); return 6; }

                Catatan.Tulis("mode admin dipasang: " + exe);
                Pesan("Mode admin terpasang.", false);
                return 0;
            }
            catch (Exception e)
            {
                Pesan("Gagal: " + e.Message, true);
                return 7;
            }
        }

        static void Pesan(string teks, bool galat)
        {
            MessageBox.Show(teks, "Kunci Keyboard", MessageBoxButtons.OK,
                galat ? MessageBoxIcon.Error : MessageBoxIcon.Information);
        }
    }

    static class Catatan
    {
        public static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KunciKeyboard");
        static readonly object gembok = new object();
        static string tanda;

        public static void Tulis(string pesan)
        {
            try
            {
                lock (gembok)
                {
                    if (tanda == null) tanda = (Tugas.Admin() ? "admin" : "biasa") + ", pid " + Process.GetCurrentProcess().Id;
                    Directory.CreateDirectory(Folder);
                    string f = Path.Combine(Folder, "catatan.txt");
                    FileInfo info = new FileInfo(f);
                    if (info.Exists && info.Length > 256 * 1024)
                    {
                        string lama = f + ".lama";
                        if (File.Exists(lama)) File.Delete(lama);
                        File.Move(f, lama);
                    }
                    File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        + "  [" + tanda + "]  " + pesan + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    // Jendela hampir tembus pandang selebar semua layar: menelan sentuhan layar sentuh, layar tetap terlihat.
    class Tirai : Form
    {
        readonly Pengunci pemilik;
        public bool BolehTutup;

        public Tirai(Pengunci pemilik)
        {
            this.pemilik = pemilik;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.01;
            Bounds = SystemInformation.VirtualScreen;
            Text = "Kunci Keyboard";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_POINTERDOWN:
                case Native.WM_LBUTTONDOWN:
                case Native.WM_RBUTTONDOWN:
                    pemilik.Sentuh();
                    break;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!BolehTutup) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }

    // Lencana kecil di bawah layar, jendela berlapis dengan alpha per piksel.
    class Lencana : Form
    {
        readonly float skala;
        readonly System.Windows.Forms.Timer anim;
        Bitmap gambar;
        IntPtr hbmp;
        int x, y;
        float alpha, target;

        public Lencana(float skala)
        {
            this.skala = skala;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            anim = new System.Windows.Forms.Timer();
            anim.Interval = 15;
            anim.Tick += Langkah;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                            | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void Atur(Isi isi)
        {
            if (hbmp != IntPtr.Zero) { Native.DeleteObject(hbmp); hbmp = IntPtr.Zero; }
            if (gambar != null) gambar.Dispose();
            gambar = Gambar.Lencana(isi, skala);
            hbmp = gambar.GetHbitmap(Color.FromArgb(0));
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            x = area.Left + (area.Width - gambar.Width) / 2;
            y = area.Bottom - gambar.Height - (int)(20 * skala);
            Perbarui();
        }

        public void Tampil() { target = 255; anim.Start(); }
        public void Pudar() { if (target != 0) { target = 0; anim.Start(); } }

        void Langkah(object sender, EventArgs e)
        {
            float laju = target > alpha ? 0.30f : 0.12f;
            alpha += (target - alpha) * laju;
            if (Math.Abs(target - alpha) < 2f) { alpha = target; anim.Stop(); }
            Perbarui();
        }

        void Perbarui()
        {
            if (hbmp == IntPtr.Zero || !IsHandleCreated) return;
            IntPtr layar = Native.GetDC(IntPtr.Zero);
            IntPtr mem = Native.CreateCompatibleDC(layar);
            IntPtr lama = Native.SelectObject(mem, hbmp);
            try
            {
                Native.POINT tujuan = new Native.POINT(x, y);
                Native.POINT asal = new Native.POINT(0, 0);
                Native.SIZE ukuran = new Native.SIZE(gambar.Width, gambar.Height);
                Native.BLENDFUNCTION campur = new Native.BLENDFUNCTION();
                campur.BlendOp = 0;
                campur.SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(alpha)));
                campur.AlphaFormat = 1;
                Native.UpdateLayeredWindow(Handle, layar, ref tujuan, ref ukuran, mem, ref asal, 0, ref campur, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(mem, lama);
                Native.DeleteDC(mem);
                Native.ReleaseDC(IntPtr.Zero, layar);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (hbmp != IntPtr.Zero) { Native.DeleteObject(hbmp); hbmp = IntPtr.Zero; }
            if (disposing && gambar != null) gambar.Dispose();
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ gambar

    class Isi
    {
        public bool GembokBuka;
        public Color Terang, Gelap, Aksen;
        public string Judul;
        public List<KeyValuePair<string, bool>> Sub = new List<KeyValuePair<string, bool>>(); // bool = tutsan

        public static Isi Hitung(int n)
        {
            Isi i = new Isi();
            i.GembokBuka = true;
            i.Terang = Color.FromArgb(244, 244, 245); i.Gelap = Color.FromArgb(161, 161, 170); i.Aksen = Color.FromArgb(212, 212, 216);
            i.Judul = "Mengunci dalam " + n;
            i.Sub.Add(new KeyValuePair<string, bool>("Esc", true));
            i.Sub.Add(new KeyValuePair<string, bool>("batal", false));
            return i;
        }

        public static Isi Terkunci(string kata, int detik)
        {
            Isi i = new Isi();
            i.Terang = Color.FromArgb(253, 224, 71); i.Gelap = Color.FromArgb(217, 119, 6); i.Aksen = Color.FromArgb(245, 158, 11);
            i.Judul = "Terkunci";
            i.Sub.Add(new KeyValuePair<string, bool>("ketik", false));
            i.Sub.Add(new KeyValuePair<string, bool>(kata, true));
            if (detik >= 0)
            {
                i.Sub.Add(new KeyValuePair<string, bool>("·", false));
                i.Sub.Add(new KeyValuePair<string, bool>((detik / 60) + ":" + (detik % 60).ToString("00"), false));
            }
            return i;
        }

        public static Isi Terbuka()
        {
            Isi i = new Isi();
            i.GembokBuka = true;
            i.Terang = Color.FromArgb(134, 239, 172); i.Gelap = Color.FromArgb(22, 163, 74); i.Aksen = Color.FromArgb(34, 197, 94);
            i.Judul = "Terbuka";
            return i;
        }
    }

    static class Gambar
    {
        static readonly Color Teks = Color.FromArgb(250, 250, 250);
        static readonly Color TeksRedup = Color.FromArgb(161, 161, 170);

        public static Bitmap Lencana(Isi isi, float s)
        {
            using (Font fJudul = new Font("Segoe UI Semibold", 15f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font fSub = new Font("Segoe UI", 12.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font fTuts = new Font("Segoe UI Semibold", 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

                float m = 18 * s, h = 56 * s, pad = 8 * s, d = 40 * s, jarak = 12 * s, kanan = 24 * s;
                float celah = 6 * s, tutsPad = 6 * s, tutsT = 18 * s;
                bool adaSub = isi.Sub.Count > 0;

                float lebarJudul, lebarSub = 0;
                using (Bitmap uk = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(uk))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    lebarJudul = Ukur(g, isi.Judul, fJudul, sf);
                    for (int i = 0; i < isi.Sub.Count; i++)
                    {
                        KeyValuePair<string, bool> seg = isi.Sub[i];
                        float lw = seg.Value ? Ukur(g, seg.Key, fTuts, sf) + tutsPad * 2 : Ukur(g, seg.Key, fSub, sf);
                        lebarSub += lw + (i > 0 ? celah : 0);
                    }
                }

                float w = pad + d + jarak + Math.Max(lebarJudul, lebarSub) + kanan;
                Bitmap bmp = new Bitmap((int)Math.Ceiling(w + m * 2), (int)Math.Ceiling(h + m * 2 + 4 * s), PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    RectangleF pil = new RectangleF(m, m, w, h);

                    // bayangan lembut
                    for (int i = 18; i >= 1; i--)
                    {
                        RectangleF r = pil;
                        r.Inflate(i * 0.75f * s, i * 0.75f * s);
                        r.Offset(0, 3 * s);
                        using (GraphicsPath p = Bulat(r, r.Height / 2))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(4, 0, 0, 0)))
                            g.FillPath(b, p);
                    }

                    // badan pil
                    using (GraphicsPath p = Bulat(pil, h / 2))
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(Lebarkan(pil),
                            Color.FromArgb(242, 39, 39, 44), Color.FromArgb(242, 20, 20, 23), 90f))
                            g.FillPath(b, p);
                        using (Pen tepi = new Pen(Color.FromArgb(26, 255, 255, 255), Math.Max(1f, s)))
                            g.DrawPath(tepi, p);
                    }

                    // wadah ikon
                    RectangleF bul = new RectangleF(m + pad, m + (h - d) / 2, d, d);
                    using (LinearGradientBrush b = new LinearGradientBrush(Lebarkan(bul),
                        Color.FromArgb(66, 66, 74), Color.FromArgb(32, 32, 37), 90f))
                        g.FillEllipse(b, bul);
                    using (GraphicsPath pendar = new GraphicsPath())
                    {
                        RectangleF pr = bul;
                        pr.Inflate(-d * 0.08f, -d * 0.08f);
                        pendar.AddEllipse(pr);
                        using (PathGradientBrush pb = new PathGradientBrush(pendar))
                        {
                            pb.CenterColor = Color.FromArgb(95, isi.Aksen);
                            pb.SurroundColors = new Color[] { Color.FromArgb(0, isi.Aksen) };
                            pb.CenterPoint = new PointF(pr.X + pr.Width / 2, pr.Y + pr.Height * 0.6f);
                            g.FillPath(pb, pendar);
                        }
                    }
                    using (Pen bibir = new Pen(Color.FromArgb(30, 255, 255, 255), Math.Max(1f, s)))
                        g.DrawEllipse(bibir, bul);

                    Gembok(g, bul.X + d / 2, bul.Y + d / 2 + (isi.GembokBuka ? 0.05f * d : 0.01f * d), 0.5f * d,
                        isi.GembokBuka, isi.Terang, isi.Gelap);

                    // teks
                    float tx = bul.Right + jarak;
                    using (SolidBrush bTeks = new SolidBrush(Teks))
                    using (SolidBrush bRedup = new SolidBrush(TeksRedup))
                    {
                        float tJudul = fJudul.GetHeight(g);
                        if (!adaSub)
                        {
                            g.DrawString(isi.Judul, fJudul, bTeks, tx, m + (h - tJudul) / 2, sf);
                        }
                        else
                        {
                            g.DrawString(isi.Judul, fJudul, bTeks, tx, m + 8 * s, sf);

                            float baris = m + 31 * s;
                            float tSub = fSub.GetHeight(g), tTuts = fTuts.GetHeight(g);
                            float x = tx;
                            for (int i = 0; i < isi.Sub.Count; i++)
                            {
                                KeyValuePair<string, bool> seg = isi.Sub[i];
                                if (i > 0) x += celah;
                                if (seg.Value)
                                {
                                    float tw = Ukur(g, seg.Key, fTuts, sf);
                                    RectangleF kr = new RectangleF(x, baris + (tSub - tutsT) / 2 - 0.5f * s, tw + tutsPad * 2, tutsT);
                                    Tuts(g, kr, s);
                                    g.DrawString(seg.Key, fTuts, bTeks, kr.X + tutsPad, kr.Y + (kr.Height - tTuts) / 2 - 0.5f * s, sf);
                                    x += kr.Width;
                                }
                                else
                                {
                                    g.DrawString(seg.Key, fSub, bRedup, x, baris, sf);
                                    x += Ukur(g, seg.Key, fSub, sf);
                                }
                            }
                        }
                    }
                }
                return bmp;
            }
        }

        static void Tuts(Graphics g, RectangleF r, float s)
        {
            float rad = 5 * s;
            RectangleF bawah = r;
            bawah.Offset(0, 1.6f * s);
            using (GraphicsPath p = Bulat(bawah, rad))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.FillPath(b, p);
            using (GraphicsPath p = Bulat(r, rad))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(Lebarkan(r),
                    Color.FromArgb(78, 78, 86), Color.FromArgb(54, 54, 61), 90f))
                    g.FillPath(b, p);
                using (Pen tepi = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1f, 0.8f * s)))
                    g.DrawPath(tepi, p);
            }
        }

        // Gembok bervolume: belenggu logam + badan bergradasi + kilau + lubang kunci.
        // (cx, cy) = tengah, k = tinggi total gembok terkunci.
        public static void Gembok(Graphics g, float cx, float cy, float k, bool terbuka, Color terang, Color gelap)
        {
            float t = 0.15f * k;
            float bw = 0.84f * k, bh = 0.58f * k;
            float bx = cx - bw / 2, by = cy - 0.08f * k;
            float r = (0.62f * k - t) / 2;
            float busurY = cy - 0.5f * k + t / 2 + r - (terbuka ? 0.17f * k : 0);
            float kiri = cx - r, kanan = cx + r;
            float dasar = by + 0.1f * k;
            float ujungKanan = terbuka ? busurY + 0.1f * k : dasar;
            RectangleF badan = new RectangleF(bx, by, bw, bh);
            float rad = 0.14f * k;

            // bayangan badan
            RectangleF bay = badan;
            bay.Offset(0, 0.07f * k);
            using (GraphicsPath p = Bulat(bay, rad))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillPath(b, p);

            // belenggu
            using (GraphicsPath sp = new GraphicsPath())
            {
                sp.AddLine(kiri, dasar, kiri, busurY);
                sp.AddArc(cx - r, busurY - r, 2 * r, 2 * r, 180, 180);
                sp.AddLine(kanan, busurY, kanan, ujungKanan);
                RectangleF sb = new RectangleF(kiri - t, busurY - r - t, 2 * r + 2 * t, dasar - busurY + r + 2 * t);
                using (LinearGradientBrush lb = new LinearGradientBrush(sb, Color.White, Color.Gray, 0f))
                {
                    ColorBlend cb = new ColorBlend();
                    cb.Colors = new Color[] {
                        Color.FromArgb(82, 82, 91), Color.FromArgb(244, 244, 245), Color.FromArgb(212, 212, 216),
                        Color.FromArgb(161, 161, 170), Color.FromArgb(244, 244, 245), Color.FromArgb(82, 82, 91) };
                    cb.Positions = new float[] { 0f, 0.22f, 0.4f, 0.62f, 0.8f, 1f };
                    lb.InterpolationColors = cb;
                    using (Pen pen = new Pen(lb, t))
                    {
                        pen.StartCap = LineCap.Flat;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, sp);
                    }
                }
            }

            // badan
            using (GraphicsPath bp = Bulat(badan, rad))
            {
                using (LinearGradientBrush lb = new LinearGradientBrush(Lebarkan(badan), terang, gelap, 90f))
                    g.FillPath(lb, bp);

                Region klipLama = g.Clip;
                g.SetClip(bp, CombineMode.Intersect);
                RectangleF kilau = new RectangleF(bx, by, bw, bh * 0.48f);
                using (LinearGradientBrush kb = new LinearGradientBrush(Lebarkan(kilau),
                    Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(kb, kilau);
                RectangleF dasarGelap = new RectangleF(bx, by + bh * 0.72f, bw, bh * 0.28f);
                using (LinearGradientBrush db = new LinearGradientBrush(Lebarkan(dasarGelap),
                    Color.FromArgb(0, 0, 0, 0), Color.FromArgb(60, 0, 0, 0), 90f))
                    g.FillRectangle(db, dasarGelap);
                g.Clip = klipLama;

                using (Pen tepi = new Pen(Color.FromArgb(150, Redupkan(gelap, 0.45f)), Math.Max(0.8f, 0.03f * k)))
                    g.DrawPath(tepi, bp);
            }

            // lubang kunci, terukir: kilau tipis di bawahnya
            float lr = 0.08f * k, lx = cx, ly = by + bh * 0.42f;
            for (int lapis = 0; lapis < 2; lapis++)
            {
                float geser = lapis == 0 ? Math.Max(0.6f, 0.02f * k) : 0;
                using (GraphicsPath lp = new GraphicsPath(FillMode.Winding))
                {
                    lp.AddEllipse(lx - lr, ly - lr + geser, lr * 2, lr * 2);
                    lp.AddRectangle(new RectangleF(lx - 0.035f * k, ly + geser, 0.07f * k, 0.18f * k));
                    Color c = lapis == 0 ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(200, Redupkan(gelap, 0.3f));
                    using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, lp);
                }
            }
        }

        // ikon aplikasi: gembok terkunci dengan bayangan jatuh
        public static Bitmap Ikon(int ukuran)
        {
            Bitmap bmp = new Bitmap(ukuran, ukuran, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = ukuran;
                if (ukuran >= 32)
                {
                    using (GraphicsPath p = new GraphicsPath())
                    {
                        RectangleF el = new RectangleF(s * 0.16f, s * 0.86f, s * 0.68f, s * 0.1f);
                        p.AddEllipse(el);
                        using (PathGradientBrush pb = new PathGradientBrush(p))
                        {
                            pb.CenterColor = Color.FromArgb(90, 0, 0, 0);
                            pb.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                            g.FillPath(pb, p);
                        }
                    }
                }
                Gembok(g, s / 2, s * 0.47f, s * 0.86f, false, Color.FromArgb(253, 224, 71), Color.FromArgb(217, 119, 6));
            }
            return bmp;
        }

        public static void SimpanIkon(string path)
        {
            int[] ukuran = { 256, 64, 48, 32, 24, 16 };
            List<byte[]> data = new List<byte[]>();
            foreach (int u in ukuran)
            {
                using (Bitmap b = Ikon(u))
                {
                    if (u == 256)
                    {
                        using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); data.Add(ms.ToArray()); }
                    }
                    else data.Add(KeDib(b));
                }
            }
            using (FileStream fs = File.Create(path))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)ukuran.Length);
                int offset = 6 + 16 * ukuran.Length;
                for (int i = 0; i < ukuran.Length; i++)
                {
                    byte sisi = (byte)(ukuran[i] >= 256 ? 0 : ukuran[i]);
                    w.Write(sisi); w.Write(sisi); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(data[i].Length); w.Write(offset);
                    offset += data[i].Length;
                }
                foreach (byte[] d in data) w.Write(d);
            }
        }

        static byte[] KeDib(Bitmap b)
        {
            int w = b.Width, h = b.Height;
            int baris = ((w + 31) / 32) * 4;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
                bw.Write(0); bw.Write(w * h * 4 + baris * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                for (int y = h - 1; y >= 0; y--)
                    for (int x = 0; x < w; x++)
                    {
                        Color c = b.GetPixel(x, y);
                        bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                    }
                bw.Write(new byte[baris * h]);
                bw.Flush();
                return ms.ToArray();
            }
        }

        static float Ukur(Graphics g, string teks, Font f, StringFormat sf)
        {
            return g.MeasureString(teks, f, PointF.Empty, sf).Width;
        }

        static RectangleF Lebarkan(RectangleF r)
        {
            r.Inflate(1, 1);
            return r;
        }

        static Color Redupkan(Color c, float f)
        {
            return Color.FromArgb((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
        }

        public static GraphicsPath Bulat(RectangleF r, float rad)
        {
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2);
            float d = rad * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ------------------------------------------------------------------ Win32

    static class Native
    {
        public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        public const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        public const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONUP = 0x0208, WM_XBUTTONUP = 0x020C, WM_POINTERDOWN = 0x0246;
        public const uint WM_QUIT = 0x0012, WM_TIMER = 0x0113, PM_NOREMOVE = 0;
        public const int THREAD_PRIORITY_TIME_CRITICAL = 15;
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
        public const int ULW_ALPHA = 2;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        public static extern uint GetWindowProcessId(IntPtr hWnd, out uint pid);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] public static extern bool SetThreadPriority(IntPtr thread, int priority);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr id, uint ms, IntPtr proc);
        [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hWnd, UIntPtr id);

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desk);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int length, out int needed);

        // false bila input sedang di desktop aman (Ctrl+Alt+Del, layar kunci, UAC). Desktop itu milik
        // SYSTEM: tidak bisa dibuka bahkan oleh proses admin.
        public static bool DesktopBiasa()
        {
            IntPtr h = OpenInputDesktop(0, false, 0x0001 /* DESKTOP_READOBJECTS */);
            if (h == IntPtr.Zero) return false;
            try
            {
                StringBuilder nama = new StringBuilder(64);
                int perlu;
                if (!GetUserObjectInformation(h, 2 /* UOI_NAME */, nama, nama.Capacity * 2, out perlu)) return true;
                string n = nama.ToString();
                return n.Equals("Default", StringComparison.OrdinalIgnoreCase)
                    || n.Equals("Screen-saver", StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseDesktop(h); }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int W, H; public SIZE(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    }
}
