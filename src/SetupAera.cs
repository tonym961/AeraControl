/*
 *  Setup AeraControl - installazione, un file solo
 *
 *  Chiede se il computer e' il server o un client e fa il necessario.
 *
 *  Gli eseguibili sono gia' compilati e incorporati qui dentro come
 *  risorse: sulle macchine di destinazione non serve quindi il
 *  compilatore C#, che i vecchi .bat richiedevano.
 *
 *  Compatibile C# 5 / .NET Framework 4.x
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("AeraControl - installazione")]
[assembly: AssemblyCompany("IOTATEC srl")]
[assembly: AssemblyProduct("AeraControl")]
[assembly: AssemblyVersion(SetupAera.Versione.Numero + ".0")]
[assembly: AssemblyFileVersion(SetupAera.Versione.Numero + ".0")]

namespace SetupAera
{
    public static class Versione
    {
        public const string Numero = "1.4.0";
    }

    // ------------------------------------------------------------------
    // Aspetto, uguale a quello della console
    // ------------------------------------------------------------------
    public static class Stile
    {
        public static readonly Color Sfondo     = Color.FromArgb(244, 246, 248);
        public static readonly Color Testata    = Color.FromArgb(34, 48, 63);
        public static readonly Color Bordo      = Color.FromArgb(226, 230, 235);
        public static readonly Color Testo      = Color.FromArgb(30, 41, 51);
        public static readonly Color TestoTenue = Color.FromArgb(107, 118, 132);
        public static readonly Color Verde      = Color.FromArgb(38, 148, 88);
        public static readonly Color Blu        = Color.FromArgb(46, 110, 180);
        public static readonly Color Rosso      = Color.FromArgb(190, 76, 70);

        public static GraphicsPath Tondo(Rectangle r, int raggio)
        {
            var p = new GraphicsPath();
            int d = raggio * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Mescola(Color a, Color b, float q)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * q),
                                  (int)(a.G + (b.G - a.G) * q),
                                  (int)(a.B + (b.B - a.B) * q));
        }
        public static Color Schiarisci(Color c, float q) { return Mescola(c, Color.White, q); }
        public static Color Scurisci(Color c, float q)   { return Mescola(c, Color.Black, q); }
    }

    public class PulsanteTondo : Control, IButtonControl
    {
        private Color tinta;
        private bool sotto, premuto;
        public bool Contorno = false;

        public PulsanteTondo(Color colore)
        {
            tinta = colore;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        }

        protected override void OnMouseEnter(EventArgs e) { sotto = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { sotto = false; premuto = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { premuto = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { premuto = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        // Deriva da Control e non da Button: la classe Button disegna
        // un proprio bordo dopo OnPaint, e sui lati alto e sinistro
        // lasciava una linea piu' scura della finestra stessa. Cosi'
        // dipinge soltanto il codice qui sotto.
        private DialogResult risultato = DialogResult.None;

        public DialogResult DialogResult
        {
            get { return risultato; }
            set { risultato = value; }
        }

        public void NotifyDefault(bool valore) { }

        public void PerformClick()
        {
            if (Enabled) OnClick(EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (risultato == DialogResult.None) return;
            Form f = FindForm();
            if (f != null) f.DialogResult = risultato;
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Senza questo resta un pixel scuro proprio nell'angolo
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Color dietro = (Parent != null) ? Parent.BackColor : Stile.Sfondo;
            using (var f = new SolidBrush(dietro)) g.FillRectangle(f, ClientRectangle);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color testo;

            using (GraphicsPath gp = Stile.Tondo(r, 8))
            {
                if (Contorno)
                {
                    Color fondo = Enabled
                        ? (premuto ? Color.FromArgb(238, 241, 245)
                                   : (sotto ? Color.FromArgb(247, 249, 251) : Color.White))
                        : Color.FromArgb(246, 247, 249);
                    using (var f = new SolidBrush(fondo)) g.FillPath(f, gp);
                    // Il riempimento lascia una cucitura piu' scura sui
                    // lati alto e sinistro: si ripassa il contorno con
                    // lo stesso colore per chiuderla.
                    using (var pen = new Pen(fondo, 1.4f)) g.DrawPath(pen, gp);
                    using (var pen = new Pen(Color.FromArgb(206, 212, 220))) g.DrawPath(pen, gp);
                    testo = Enabled ? Stile.Testo : Color.FromArgb(170, 178, 188);
                }
                else
                {
                    Color fondo;
                    if (!Enabled)     fondo = Stile.Mescola(tinta, dietro, 0.62f);
                    else if (premuto) fondo = Stile.Scurisci(tinta, 0.14f);
                    else if (sotto)   fondo = Stile.Schiarisci(tinta, 0.14f);
                    else              fondo = tinta;
                    using (var f = new SolidBrush(fondo)) g.FillPath(f, gp);
                    // Il riempimento lascia una cucitura piu' scura sui
                    // lati alto e sinistro: si ripassa il contorno con
                    // lo stesso colore per chiuderla.
                    using (var pen = new Pen(fondo, 1.4f)) g.DrawPath(pen, gp);
                    testo = Enabled ? Color.White : Stile.Mescola(Color.White, dietro, 0.45f);
                }
            }

            var rt = new Rectangle(16, 0, Width - 24, Height);
            TextRenderer.DrawText(g, Text, Font, rt, testo,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak);
        }
    }

    public class Riquadro : Panel
    {
        public int Raggio = 10;
        public Color Bordo = Stile.Bordo;

        public Riquadro()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var f = new SolidBrush(Parent != null ? Parent.BackColor : Stile.Sfondo))
                g.FillRectangle(f, ClientRectangle);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath gp = Stile.Tondo(r, Raggio))
            {
                using (var f = new SolidBrush(BackColor)) g.FillPath(f, gp);
                using (var pen = new Pen(Bordo)) g.DrawPath(pen, gp);
            }
        }
    }

    // ------------------------------------------------------------------
    // Contenuto incorporato
    // ------------------------------------------------------------------
    public static class Risorse
    {
        public static byte[] Leggi(string nome)
        {
            Assembly a = Assembly.GetExecutingAssembly();
            using (Stream s = a.GetManifestResourceStream(nome))
            {
                if (s == null) return null;
                var m = new MemoryStream();
                var buf = new byte[65536];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) m.Write(buf, 0, n);
                return m.ToArray();
            }
        }

        public static bool Scrivi(string nome, string destinazione, out string errore)
        {
            errore = "";
            try
            {
                byte[] dati = Leggi(nome);
                if (dati == null) { errore = "risorsa " + nome + " non trovata"; return false; }
                File.WriteAllBytes(destinazione, dati);
                return true;
            }
            catch (Exception ex) { errore = ex.Message; return false; }
        }
    }

    // ------------------------------------------------------------------
    // Finestra
    // ------------------------------------------------------------------
    public class FormSetup : Form
    {
        public const string Cartella = @"C:\iotatau\AeraControl";

        private TextBox txtLog;
        private PulsanteTondo btnServer, btnClient, btnRimuovi, btnChiudi;
        private Label lblStato;

        private const int Largo = 620;

        public FormSetup()
        {
            Text = "AeraControl - installazione  " + Versione.Numero;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Stile.Sfondo;
            Font = new Font("Segoe UI", 9F);
            DoubleBuffered = true;
            ClientSize = new Size(Largo, 520);
            IconaDaRisorsa();

            var testata = new Panel();
            testata.Location = new Point(0, 0);
            testata.Size = new Size(Largo, 72);
            testata.BackColor = Stile.Testata;
            Controls.Add(testata);

            var titolo = new Label();
            titolo.Text = "AeraControl";
            titolo.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            titolo.ForeColor = Color.White;
            titolo.BackColor = Color.Transparent;
            titolo.Location = new Point(20, 12);
            titolo.Size = new Size(340, 30);
            testata.Controls.Add(titolo);

            var sotto = new Label();
            sotto.Text = "installazione su questo computer";
            sotto.ForeColor = Color.FromArgb(160, 175, 195);
            sotto.BackColor = Color.Transparent;
            sotto.Font = new Font("Segoe UI", 8.5F);
            sotto.Location = new Point(21, 44);
            sotto.Size = new Size(400, 18);
            testata.Controls.Add(sotto);

            lblStato = new Label();
            lblStato.TextAlign = ContentAlignment.MiddleRight;
            lblStato.ForeColor = Color.FromArgb(160, 175, 195);
            lblStato.BackColor = Color.Transparent;
            lblStato.Font = new Font("Segoe UI", 8.5F);
            lblStato.Location = new Point(Largo - 20 - 280, 44);
            lblStato.Size = new Size(280, 18);
            testata.Controls.Add(lblStato);

            var domanda = new Label();
            domanda.Text = "CHE RUOLO HA QUESTO COMPUTER?";
            domanda.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            domanda.ForeColor = Stile.TestoTenue;
            domanda.Location = new Point(22, 88);
            domanda.Size = new Size(400, 18);
            Controls.Add(domanda);

            btnServer = new PulsanteTondo(Stile.Verde);
            btnServer.Text = "SERVER\r\nQui girano gli applicativi e compaiono le finestre";
            btnServer.Location = new Point(20, 110);
            btnServer.Size = new Size(Largo - 40, 62);
            btnServer.Click += delegate { InBackground(InstallaServer); };
            Controls.Add(btnServer);

            btnClient = new PulsanteTondo(Stile.Blu);
            btnClient.Text = "CLIENT\r\nDa qui si comandano gli applicativi che stanno sul server";
            btnClient.Location = new Point(20, 180);
            btnClient.Size = new Size(Largo - 40, 62);
            btnClient.Click += delegate { InBackground(InstallaClient); };
            Controls.Add(btnClient);

            var cornice = new Riquadro();
            cornice.Location = new Point(20, 256);
            cornice.Size = new Size(Largo - 40, 186);
            Controls.Add(cornice);

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.Location = new Point(12, 10);
            txtLog.Size = new Size(cornice.Width - 24, 166);
            txtLog.BackColor = Color.White;
            txtLog.ForeColor = Color.FromArgb(60, 72, 86);
            txtLog.Font = new Font("Consolas", 8.5F);
            cornice.Controls.Add(txtLog);

            btnRimuovi = new PulsanteTondo(Color.White);
            btnRimuovi.Contorno = true;
            btnRimuovi.Text = "Rimuovi";
            btnRimuovi.Location = new Point(20, 454);
            btnRimuovi.Size = new Size(110, 36);
            btnRimuovi.Click += delegate { InBackground(Rimuovi); };
            Controls.Add(btnRimuovi);

            btnChiudi = new PulsanteTondo(Color.White);
            btnChiudi.Contorno = true;
            btnChiudi.Text = "Chiudi";
            btnChiudi.Location = new Point(Largo - 20 - 110, 454);
            btnChiudi.Size = new Size(110, 36);
            btnChiudi.Click += delegate { Close(); };
            Controls.Add(btnChiudi);

            var firma = new Label();
            firma.Text = "IOTATEC srl";
            firma.Font = new Font("Segoe UI", 8F);
            firma.ForeColor = Stile.TestoTenue;
            firma.Location = new Point(150, 465);
            firma.Size = new Size(200, 16);
            Controls.Add(firma);

            Shown += delegate { Presentazione(); };
        }

        private void IconaDaRisorsa()
        {
            try
            {
                byte[] d = Risorse.Leggi("icona.ico");
                if (d != null) using (var m = new MemoryStream(d)) Icon = new Icon(m);
            }
            catch { }
        }

        private void Presentazione()
        {
            Log("AeraControl " + Versione.Numero + " - installazione");
            Log("");
            Log("Tutto viene installato in " + Cartella);
            Log("");
            if (Amministratore) lblStato.Text = "privilegi di amministratore: si";
            else
            {
                lblStato.Text = "senza privilegi di amministratore";
                Log("ATTENZIONE: non si hanno i privilegi di amministratore.");
                Log("Chiudere e rilanciare con il tasto destro, Esegui come");
                Log("amministratore, altrimenti l'installazione non riesce.");
                Log("");
            }
            Log("Scegliere il ruolo di questo computer.");
        }

        private static bool Amministratore
        {
            get
            {
                try
                {
                    var i = WindowsIdentity.GetCurrent();
                    return new WindowsPrincipal(i).IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        // ---------------------------------------------------- utilita'
        private void Log(string testo)
        {
            if (txtLog.InvokeRequired) { txtLog.BeginInvoke((MethodInvoker)delegate { Log(testo); }); return; }
            txtLog.AppendText(testo + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private void Comandi(bool attivi)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { Comandi(attivi); }); return; }
            btnServer.Enabled = attivi;
            btnClient.Enabled = attivi;
            btnRimuovi.Enabled = attivi;
        }

        // Il lavoro gira su un thread separato: la finestra resta
        // reattiva anche mentre PowerShell configura il server.
        private void InBackground(ThreadStart lavoro)
        {
            Comandi(false);
            var t = new Thread(delegate()
            {
                try { lavoro(); }
                catch (Exception ex) { Log("Errore: " + ex.Message); }
                finally { Comandi(true); }
            });
            t.IsBackground = true;
            t.Start();
        }

        private bool PreparaCartella()
        {
            try
            {
                if (!Directory.Exists(Cartella)) Directory.CreateDirectory(Cartella);
                return true;
            }
            catch (Exception ex)
            {
                Log("Impossibile creare " + Cartella);
                Log("  " + ex.Message);
                return false;
            }
        }

        private static void ChiudiSeAperto(string nome)
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName(nome))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }
            catch { }
        }

        // ---------------------------------------------------- client
        private void InstallaClient()
        {
            Log("");
            Log("=== INSTALLAZIONE CLIENT ===");

            if (!Amministratore)
            {
                Log("Servono i privilegi di amministratore. Rilanciare come tale.");
                return;
            }
            if (!PreparaCartella()) return;

            ChiudiSeAperto("AeraControl");

            string guaio;
            if (!Risorse.Scrivi("AeraControl.exe", Path.Combine(Cartella, "AeraControl.exe"), out guaio))
            { Log("[X] console: " + guaio); return; }
            Log("[OK] console installata");

            // Serve all'icona della finestra: contiene tutte le misure
            if (Risorse.Scrivi("icona.ico", Path.Combine(Cartella, "icona.ico"), out guaio))
                Log("[OK] icona");

            Collegamenti();
            VoceDisinstalla();
            PuliziaVecchie();

            Log("");
            Log("Fatto. Aprire AeraControl dal collegamento sul desktop e");
            Log("premere Configura per indicare server, utente e password.");
        }

        private void Collegamenti()
        {
            string bersaglio = Path.Combine(Cartella, "AeraControl.exe");
            var dove = new List<string>();
            dove.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            dove.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));

            int fatti = 0;
            foreach (string c in dove)
            {
                if (string.IsNullOrEmpty(c) || !Directory.Exists(c)) continue;
                try
                {
                    Collegamento(Path.Combine(c, "AeraControl.lnk"), bersaglio);
                    fatti++;
                }
                catch { }
            }
            Log(fatti > 0 ? "[OK] collegamenti creati" : "[!]  collegamenti non creati");
        }

        // Late binding su WScript.Shell: evita di referenziare la
        // libreria COM in fase di compilazione.
        private static void Collegamento(string lnk, string bersaglio)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            object shell = Activator.CreateInstance(t);
            object c = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                                      null, shell, new object[] { lnk });
            Type tc = c.GetType();
            BindingFlags set = BindingFlags.SetProperty;
            tc.InvokeMember("TargetPath", set, null, c, new object[] { bersaglio });
            tc.InvokeMember("WorkingDirectory", set, null, c, new object[] { Cartella });
            tc.InvokeMember("IconLocation", set, null, c, new object[] { bersaglio + ",0" });
            tc.InvokeMember("Description", set, null, c,
                            new object[] { "Avvio e controllo degli applicativi Aera" });
            tc.InvokeMember("Save", BindingFlags.InvokeMethod, null, c, null);
        }

        private void VoceDisinstalla()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AeraControl"))
                {
                    if (k == null) return;
                    k.SetValue("DisplayName", "AeraControl");
                    k.SetValue("DisplayVersion", Versione.Numero);
                    k.SetValue("Publisher", "IOTATEC srl");
                    k.SetValue("InstallLocation", Cartella);
                    k.SetValue("DisplayIcon", Path.Combine(Cartella, "AeraControl.exe"));
                    k.SetValue("UninstallString",
                               "\"" + Path.Combine(Cartella, "Setup-AeraControl.exe") + "\"");
                    k.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
                Log("[OK] voce in App e funzionalita'");
            }
            catch { }
        }

        // Le prime versioni si installavano in cartelle separate
        private void PuliziaVecchie()
        {
            foreach (string v in new string[] { @"C:\iotatau\Palmari", @"C:\iotatau\AeraTray" })
            {
                try
                {
                    if (!Directory.Exists(v)) continue;
                    Directory.Delete(v, true);
                    Log("[OK] rimossa la vecchia cartella " + v);
                }
                catch { }
            }
        }

        // ---------------------------------------------------- server
        private void InstallaServer()
        {
            Log("");
            Log("=== INSTALLAZIONE SERVER ===");

            if (!Amministratore)
            {
                Log("Servono i privilegi di amministratore. Rilanciare come tale.");
                return;
            }
            if (!PreparaCartella()) return;

            ChiudiSeAperto("AeraTray");

            string guaio;
            if (!Risorse.Scrivi("AeraTray.exe", Path.Combine(Cartella, "AeraTray.exe"), out guaio))
            { Log("[X] segnalatore: " + guaio); return; }
            Log("[OK] segnalatore installato");

            if (Risorse.Scrivi("icona.ico", Path.Combine(Cartella, "icona.ico"), out guaio))
                Log("[OK] icona");

            // La console serve anche sul server, per provare da li'
            if (Risorse.Scrivi("AeraControl.exe", Path.Combine(Cartella, "AeraControl.exe"), out guaio))
                Log("[OK] console installata");

            PuliziaVecchie();

            Log("");
            Log("Configurazione del server in corso: attivita' pianificate,");
            Log("UAC di rete, profilo di rete e firewall.");
            Log("Si apre una finestra a parte: seguire le domande.");
            Log("");

            string ps = Path.Combine(Path.GetTempPath(), "AeraSetupServer.ps1");
            if (!Risorse.Scrivi("ServerSetup.ps1", ps, out guaio))
            { Log("[X] script del server: " + guaio); return; }

            try
            {
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -File \"" + ps + "\"");
                psi.UseShellExecute = true;
                using (Process p = Process.Start(psi))
                {
                    if (p != null) p.WaitForExit();
                }
                Log("Configurazione del server terminata.");
            }
            catch (Exception ex) { Log("[X] " + ex.Message); }

            try { File.Delete(ps); } catch { }
        }

        // --------------------------------------------------- rimozione
        private void Rimuovi()
        {
            Log("");
            Log("=== RIMOZIONE ===");

            if (!Amministratore)
            {
                Log("Servono i privilegi di amministratore. Rilanciare come tale.");
                return;
            }

            ChiudiSeAperto("AeraControl");
            ChiudiSeAperto("AeraTray");

            // I segnaposto hanno il nome degli applicativi: si tolgono
            // solo quelli che stanno nella nostra cartella.
            foreach (string n in new string[] { "Aera_Service", "AeraRemoteServer",
                                                "RestaurantPocketSol" })
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(n))
                    {
                        try
                        {
                            if (p.MainModule.FileName.IndexOf("\\presenza\\",
                                    StringComparison.OrdinalIgnoreCase) >= 0)
                                p.Kill();
                        }
                        catch { }
                    }
                }
                catch { }
            }

            foreach (string c in new string[] {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
            {
                try
                {
                    string f = Path.Combine(c, "AeraControl.lnk");
                    if (File.Exists(f)) File.Delete(f);
                    string vecchio = Path.Combine(c, "Aera - Console applicativi.lnk");
                    if (File.Exists(vecchio)) File.Delete(vecchio);
                }
                catch { }
            }
            Log("[OK] collegamenti rimossi");

            try
            {
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AeraControl", false);
                Log("[OK] voce in App e funzionalita' rimossa");
            }
            catch { }

            Log("");
            Log("Restano le attivita' pianificate sul server e il");
            Log("dirottamento del pulsante Palmari, che si tolgono dalla");
            Log("console prima di rimuoverla.");
            Log("La cartella " + Cartella + " non viene cancellata.");
        }
    }

    static class Programma
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] argomenti)
        {
            try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Serve l'amministratore: si chiede subito, invece di
            // fallire a meta' installazione.
            bool giaAmmin = false;
            try
            {
                var i = WindowsIdentity.GetCurrent();
                giaAmmin = new WindowsPrincipal(i).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { }

            bool rilanciato = false;
            foreach (string a in argomenti)
                if (string.Equals(a, "/elevato", StringComparison.OrdinalIgnoreCase))
                    rilanciato = true;

            if (!giaAmmin && !rilanciato)
            {
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath, "/elevato");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                    return;
                }
                catch { /* elevazione rifiutata: si prosegue lo stesso */ }
            }

            Application.Run(new FormSetup());
        }
    }
}
