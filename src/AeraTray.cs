/*
 *  AeraTray - segnalatore di stato degli applicativi Aera
 *
 *  Gira sul server, nella sessione dell'utente connesso, e tiene
 *  un'icona accanto all'orologio che dice a colpo d'occhio se gli
 *  applicativi sono tutti attivi.
 *
 *      verde  = tutti attivi
 *      giallo = alcuni attivi
 *      rosso  = nessuno attivo
 *
 *  Legge lo stato in locale con la lista dei processi: nessuna
 *  chiamata di rete, nessun costo. Avvio e arresto passano dalle
 *  stesse attivita' pianificate che usa il client, cosi' il
 *  comportamento e' identico da qualunque parte si agisca.
 *
 *  Compatibile C# 5 / .NET Framework 4.x
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("AeraControl - Segnalatore di stato")]
[assembly: System.Reflection.AssemblyCompany("IOTATEC srl")]
[assembly: System.Reflection.AssemblyProduct("AeraTray")]
[assembly: System.Reflection.AssemblyVersion(AeraTray.Versione.Numero + ".0")]
[assembly: System.Reflection.AssemblyFileVersion(AeraTray.Versione.Numero + ".0")]

namespace AeraTray
{
    // ------------------------------------------------------------------
    // Numero unico del prodotto: lo stesso di AeraControl.cs e di
    // SetupAera.cs. La nota estesa sta in AeraControl.cs.
    // ------------------------------------------------------------------
    public static class Versione
    {
        public const string Numero = "1.6.4";
    }

    public class Applicativo
    {
        public string Titolo;
        public string Task;
        public string Processo;   // senza estensione
        public string Percorso;

        // Valorizzato solo per i servizi di Windows: si comandano con
        // sc invece che con schtasks, e lo stato lo dice il gestore
        // dei servizi, non la presenza del processo.
        public string Servizio;

        public bool EServizio { get { return Servizio != null && Servizio.Length > 0; } }

        // Se conta nel colore dell'icona. Un applicativo che su questa
        // macchina non c'e' non deve far diventare gialla la spia.
        public bool Sorvegliato = true;

        // Se e' installato: eseguibile presente, o servizio registrato
        public bool Installato = true;

        public Applicativo(string titolo, string task, string processo, string percorso)
        {
            Titolo = titolo; Task = task; Processo = processo;
            Percorso = percorso; Servizio = "";
        }

        public Applicativo(string titolo, string servizio, string processo,
                           string percorso, bool servizioDiWindows)
        {
            Titolo = titolo; Task = servizio; Processo = processo;
            Percorso = percorso; Servizio = servizioDiWindows ? servizio : "";
        }
    }

    // ------------------------------------------------------------------
    // Aspetto, in comune con la console del client
    // ------------------------------------------------------------------
    public static class Stile
    {
        public static readonly Color Sfondo     = Color.FromArgb(244, 246, 248);
        public static readonly Color Testata    = Color.FromArgb(34, 48, 63);
        public static readonly Color Bordo      = Color.FromArgb(226, 230, 235);
        public static readonly Color Testo      = Color.FromArgb(30, 41, 51);
        public static readonly Color TestoTenue = Color.FromArgb(107, 118, 132);
        public static readonly Color Verde      = Color.FromArgb(38, 148, 88);
        public static readonly Color Rosso      = Color.FromArgb(190, 76, 70);
        public static readonly Color Ambra      = Color.FromArgb(198, 132, 30);

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

        public PulsanteTondo(Color colore)
        {
            tinta = colore;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
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
            using (var f = new SolidBrush(Parent != null ? Parent.BackColor : Stile.Sfondo))
                g.FillRectangle(f, ClientRectangle);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fondo;
            if (!Enabled)     fondo = Color.FromArgb(205, 211, 219);
            else if (premuto) fondo = Stile.Scurisci(tinta, 0.14f);
            else if (sotto)   fondo = Stile.Schiarisci(tinta, 0.14f);
            else              fondo = tinta;

            using (GraphicsPath gp = Stile.Tondo(r, 5))
            {
                using (var f = new SolidBrush(fondo)) g.FillPath(f, gp);
                // Chiude la cucitura piu' scura sui lati alto e sinistro
                using (var pen = new Pen(fondo, 1.4f)) g.DrawPath(pen, gp);
            }

            TextRenderer.DrawText(g, Text, Font, r,
                Enabled ? Color.White : Color.FromArgb(243, 245, 247),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ------------------------------------------------------------------
    // Finestra di dettaglio: si apre cliccando l'icona
    // ------------------------------------------------------------------
    public class FormDettaglio : Form
    {
        private Applicativo[] applicativi;
        private Label[] lblStato;
        private PulsanteTondo[] btnAzione;
        private PictureBox[] icone;
        private System.Windows.Forms.Timer timer;

        public delegate void Comando(string verbo, string task);
        public delegate bool LeggiAvvio();
        public delegate void CambiaAvvio();
        public delegate void CambiaSorv(string task, bool sorveglia);

        private CambiaSorv cambiaSorv;
        private CheckBox[] chkSorv;
        private Comando manda;
        private LeggiAvvio leggiAvvio, leggiGuardiano;
        private CambiaAvvio cambiaAvvio, cambiaGuardiano;
        private CheckBox chkAvvio, chkGuardiano;
        private bool sistemando = false;

        private const int Largo = 430;
        private const int AltRiga = 56;

        public FormDettaglio(Applicativo[] elenco, Comando comando, Icon iconaFinestra,
                             LeggiAvvio leggi, CambiaAvvio cambia, CambiaSorv sorveglia,
                             LeggiAvvio leggiGuard, CambiaAvvio cambiaGuard)
        {
            applicativi = elenco;
            manda = comando;
            leggiAvvio = leggi;
            cambiaAvvio = cambia;
            cambiaSorv = sorveglia;
            leggiGuardiano = leggiGuard;
            cambiaGuardiano = cambiaGuard;

            Text = "AeraControl  " + Versione.Numero;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Stile.Sfondo;
            Font = new Font("Segoe UI", 9F);
            DoubleBuffered = true;
            if (iconaFinestra != null) Icon = iconaFinestra;

            int n = applicativi.Length;
            // ultima voce: 20 per la riga in calce, 26 per la seconda
            // casella di spunta
            ClientSize = new Size(Largo, 16 + n * AltRiga + 82 + 20 + 26);

            lblStato = new Label[n];
            btnAzione = new PulsanteTondo[n];
            icone = new PictureBox[n];
            chkSorv = new CheckBox[n];

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                int y = 12 + i * AltRiga;

                var scheda = new Panel();
                scheda.Location = new Point(12, y);
                scheda.Size = new Size(Largo - 24, AltRiga - 8);
                scheda.BackColor = Color.White;
                Controls.Add(scheda);

                // La spunta decide se questo pesa sul colore
                // dell'icona: una macchina puo' non avere tutto.
                var cs = new CheckBox();
                cs.Location = new Point(8, (AltRiga - 8 - 16) / 2);
                cs.Size = new Size(16, 16);
                cs.Checked = applicativi[i].Sorvegliato;
                cs.BackColor = Color.Transparent;
                cs.CheckedChanged += delegate
                {
                    if (sistemando) return;
                    if (cambiaSorv != null) cambiaSorv(applicativi[idx].Task, cs.Checked);
                    Aggiorna();
                };
                scheda.Controls.Add(cs);
                chkSorv[i] = cs;

                var pb = new PictureBox();
                pb.Location = new Point(30, 8);
                pb.Size = new Size(32, 32);
                pb.SizeMode = PictureBoxSizeMode.Zoom;
                pb.BackColor = Color.Transparent;
                pb.Image = IconaDi(applicativi[i]);
                scheda.Controls.Add(pb);
                icone[i] = pb;

                // Fin dove si puo' arrivare senza finire sotto il
                // pulsante, che sta a destra
                int largTesto = (scheda.Width - 90) - 72 - 8;

                var nome = new Label();
                nome.Text = applicativi[i].Titolo +
                            (applicativi[i].EServizio ? "   (servizio)" : "");
                nome.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
                nome.ForeColor = Stile.Testo;
                nome.AutoEllipsis = true;
                nome.Location = new Point(72, 7);
                nome.Size = new Size(largTesto, 18);
                scheda.Controls.Add(nome);

                var st = new Label();
                st.Text = "...";
                st.ForeColor = Stile.TestoTenue;
                st.Font = new Font("Segoe UI", 8.5F);
                st.AutoEllipsis = true;
                st.Location = new Point(72, 26);
                st.Size = new Size(largTesto, 16);
                scheda.Controls.Add(st);
                lblStato[i] = st;

                var b = new PulsanteTondo(Stile.Verde);
                b.Text = "Avvia";
                b.Size = new Size(78, 28);
                b.Location = new Point(scheda.Width - 90, 10);
                b.Click += delegate { Azione(idx); };
                scheda.Controls.Add(b);
                btnAzione[i] = b;
            }

            int yAvvio = 12 + n * AltRiga + 4;

            chkAvvio = new CheckBox();
            chkAvvio.Text = "Apri il segnalatore a ogni accesso a Windows";
            chkAvvio.Location = new Point(14, yAvvio);
            chkAvvio.Size = new Size(Largo - 28, 22);
            chkAvvio.ForeColor = Stile.Testo;
            chkAvvio.BackColor = Color.Transparent;
            chkAvvio.CheckedChanged += delegate
            {
                // Il cambio programmatico non deve rimbalzare indietro
                if (sistemando) return;
                if (cambiaAvvio != null) cambiaAvvio();
                LeggiSpunta();
            };
            Controls.Add(chkAvvio);

            chkGuardiano = new CheckBox();
            chkGuardiano.Text = "Riavvia gli applicativi sorvegliati se li trova fermi";
            chkGuardiano.Location = new Point(14, yAvvio + 24);
            chkGuardiano.Size = new Size(Largo - 28, 22);
            chkGuardiano.ForeColor = Stile.Testo;
            chkGuardiano.BackColor = Color.Transparent;
            chkGuardiano.CheckedChanged += delegate
            {
                if (sistemando) return;
                if (cambiaGuardiano != null) cambiaGuardiano();
                LeggiSpunta();
            };
            Controls.Add(chkGuardiano);

            int yPiede = yAvvio + 56;

            var bTutti = new PulsanteTondo(Stile.Verde);
            bTutti.Text = "Avvia tutti";
            bTutti.Size = new Size(110, 30);
            bTutti.Location = new Point(12, yPiede);
            bTutti.Click += delegate { manda("run", null); };
            Controls.Add(bTutti);

            var bChiudi = new PulsanteTondo(Color.FromArgb(90, 105, 125));
            bChiudi.Text = "Chiudi";
            bChiudi.Size = new Size(90, 30);
            bChiudi.Location = new Point(Largo - 12 - 90, yPiede);
            bChiudi.Click += delegate { Hide(); };
            Controls.Add(bChiudi);

            // In calce
            var firma = new Label();
            firma.Text = "IOTATEC srl";
            firma.Font = new Font("Segoe UI", 7.5F);
            firma.ForeColor = Stile.TestoTenue;
            firma.BackColor = Color.Transparent;
            firma.Location = new Point(14, yPiede + 34);
            firma.Size = new Size(200, 14);
            Controls.Add(firma);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 2000;
            timer.Tick += delegate { Aggiorna(); };

            // Chiudendo con la X si nasconde soltanto: il segnalatore
            // deve restare acceso accanto all'orologio.
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };

            VisibleChanged += delegate
            {
                if (Visible) { Aggiorna(); LeggiSpunta(); timer.Start(); }
                else timer.Stop();
            };
        }

        // L'eseguibile del servizio Orderman porta l'icona generica di
        // Windows, non il marchio: per quello si usa una copia
        // incorporata, presa dal programma d'installazione. Per gli
        // altri va bene quella dell'eseguibile.
        private const string IconaOrderman =
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMA" +
            "AA7DAcdvqGQAAAM5SURBVFhH1ZdZqI1RFMd/madMmZLxwZQhCgmZwpNQyJDwJOLJUMqTeJAyy1AKmYrwYCqhTMmUBxnKEMlMpgyZ" +
            "+9fap2W51z3fuecm/9qd0157r299a/3X8MF/ipbAcGA8MBkYAwwA2gLV4+GKwFzgC/Ad+AF8Az4AD4AjwGygTbxUTIwCvgI//7Ju" +
            "AXOABvFyMdARuA08sbfWrzwQjZB3DgNdooLyohrQDWhvru4ADLLQnAA+BUOuAf2ikopCHWAscCkYcRPoFQ8XitpAZ2AYMMIyoDVQ" +
            "2Z1Rpmy3MCQjzgEt3JnM0NtNA44Dz4DPlg2K/z1gS3B1PWBz8MSaYGjeUI7vyYP9z4F5rh40Ao46+WtgYNBdJloBJ8OD3gPXgQvA" +
            "XfNEkun/IiOr0NsyJcm3ZfFCDbuQLqvo7AOGAE2AuhZvVcSr7txpoLnTs8rJHloG5SBrtNEMqOIFwEjgo11U5VtpXCgJSke5e615" +
            "zUPceOv0TPLCxsB5q14H7O0EGbbDWX4MqO8vlgBVvqpx0/avOF1LvVCVTaxOwqm2LxcrztoT+Sb6SxmhlxGJ0zN2A5WSsCfwxgRK" +
            "LXU6QS5N5HlRhJK6wRmgEp3rnGJpio/KaApBJ+eZp2ZQebDRGaCumTOgK/DSuXq07YvdSjHti4iqfIVCIdjrDND/HOFVHtODtNRU" +
            "BKWgLyKbfNwyomEg4WovrGldLAmV54nJM1w9f2cELcSI/i7MWtPjgcVOqHh3t32l6BknEykL6WqqH0mHwt0jHujrMkFrvSuXsv6+" +
            "7Yu9WaccZdljp/uQhfc3iJE+T+VuDZsJSk2FqY/bywcy9qDTqzQfFw8l6E2V7+nwndBeVQWzxF9zw7owE+y3/VKx0Gp1uqBJZmg8" +
            "lAfEHRUeNa+kSzPDH7GPUKPRYJEuaYmU801pWVBuD7ahxet4ZaNaXtAQsTMokFcu2rChLGhqrqxlcW5nsd1lQ4e/K9ZPiQ8pC4r3" +
            "cteK/dIDbgBngVNWYB6VMi2pw+o7oiCoGOnz63IgUj5LWbTV+km5oUFlpsVVWeJJ6pcamT5YNIiqoaWRrGhQzPVBMgFYACwDVgBL" +
            "gFlWK9TAsqTqv8UvwrEpcW1arggAAAAASUVORK5CYII=";

        private static Image IconaDi(Applicativo a)
        {
            if (a.EServizio && IconaOrderman.Length > 20)
            {
                try
                {
                    byte[] dati = Convert.FromBase64String(IconaOrderman);
                    using (var ms = new MemoryStream(dati)) return Image.FromStream(ms);
                }
                catch { }
            }

            try
            {
                if (File.Exists(a.Percorso))
                    using (Icon ic = Icon.ExtractAssociatedIcon(a.Percorso))
                        if (ic != null) return ic.ToBitmap();
            }
            catch { }

            return null;
        }

        private static bool ServizioSu(string nome)
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", "query \"" + nome + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string t = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return false; }
                    return p.ExitCode == 0 && t.ToUpperInvariant().Contains("RUNNING");
                }
            }
            catch { return false; }
        }

        private void LeggiSpunta()
        {
            sistemando = true;
            try
            {
                if (chkAvvio != null && leggiAvvio != null)
                    chkAvvio.Checked = leggiAvvio();
                if (chkGuardiano != null && leggiGuardiano != null)
                    chkGuardiano.Checked = leggiGuardiano();
            }
            catch { }
            sistemando = false;
        }

        public void Aggiorna()
        {
            for (int i = 0; i < applicativi.Length; i++)
            {
                bool attivo;
                if (applicativi[i].EServizio) attivo = ServizioSu(applicativi[i].Servizio);
                else
                {
                    Process[] q = null;
                    try { q = Process.GetProcessesByName(applicativi[i].Processo); }
                    catch { }
                    attivo = (q != null && q.Length > 0);
                }

                Process[] p = null;
                try { p = Process.GetProcessesByName(applicativi[i].Processo); }
                catch { }

                sistemando = true;
                chkSorv[i].Checked = applicativi[i].Sorvegliato;
                sistemando = false;

                if (!applicativi[i].Installato)
                {
                    lblStato[i].ForeColor = Stile.TestoTenue;
                    lblStato[i].Text = "non installato su questa macchina";
                    btnAzione[i].Enabled = false;
                    btnAzione[i].Text = "Avvia";
                    continue;
                }

                btnAzione[i].Enabled = true;

                if (attivo)
                {
                    string dett = "in esecuzione";
                    if (p != null && p.Length > 0)
                    {
                        double mb = 0;
                        try { mb = Math.Round(p[0].WorkingSet64 / 1048576.0, 1); }
                        catch { }
                        dett = string.Format("in esecuzione - PID {0} - {1} MB", p[0].Id, mb);
                    }

                    lblStato[i].ForeColor = Stile.Verde;
                    lblStato[i].Text = dett;
                    btnAzione[i].Text = "Ferma";
                }
                else
                {
                    lblStato[i].ForeColor = Stile.TestoTenue;
                    lblStato[i].Text = applicativi[i].Sorvegliato
                                       ? "non in esecuzione"
                                       : "non in esecuzione (non sorvegliato)";
                    btnAzione[i].Text = "Avvia";
                }
            }
        }

        private void Azione(int i)
        {
            bool attivo = (btnAzione[i].Text == "Ferma");
            manda(attivo ? "end" : "run", applicativi[i].Task);
        }

        // Compare vicino all'orologio, non al centro dello schermo
        public void MostraVicinoAllOrologio()
        {
            Rectangle a = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(a.Right - Width - 12, a.Bottom - Height - 12);
            Show();
            Activate();
            BringToFront();
        }
    }

    public class Vassoio : ApplicationContext
    {
        // ---- Applicativi sorvegliati -------------------------------
        private Applicativo[] applicativi = new Applicativo[]
        {
            new Applicativo("Aera Service", "Aera_Service", "Aera_Service",
                            @"C:\Aera\Aera_Service.exe"),
            new Applicativo("Aera Remote Function", "Aera_RemoteServer", "AeraRemoteServer",
                            @"C:\Aera\Remote_Function\AeraRemoteServer.exe"),
            new Applicativo("Restaurant Pocket Sol", "Aera_RestaurantPocket", "RestaurantPocketSol",
                            @"C:\Aera\RestaurantPocketSol\RestaurantPocketSol.exe"),

            new Applicativo("Orderman Classic Proxy", "OrdermanClassicProxy",
                            "ClassicProxyService",
                            @"C:\Program Files\Orderman\ClassicProxy\ClassicProxyService.exe",
                            true)
        };
        // ------------------------------------------------------------

        private NotifyIcon icona;
        private System.Windows.Forms.Timer timer;
        private Icon iconaCorrente;
        private bool[] attivoPrima;
        private bool primoGiro = true;
        private FormDettaglio dettaglio;
        private ToolStripMenuItem voceAvvio, voceGuardiano;

        // ---- guardiano ---------------------------------------------
        // I server si riavviano per manutenzione due o tre volte a
        // settimana, e al ritorno gli applicativi non ripartono da
        // soli: le attivita' pianificate aspettano che qualcuno le
        // chiami. Con il guardiano acceso ci pensa il segnalatore.
        //
        // Il freno serve: senza, un applicativo che non parte verrebbe
        // rilanciato ogni cinque secondi all'infinito.
        private DateTime[] ultimoTentativo;
        private int[] tentativiFalliti;
        private bool[] fermatoAMano;
        private bool avvioInCorso;
        private DateTime acceso = DateTime.UtcNow;

        private const int AttesaIniziale   = 25;   // secondi dopo l'apertura
        private const int PausaFraTentativi = 60;  // secondi fra due tentativi
        private const int TentativiMax      = 3;   // dopo i quali si aspetta a lungo
        private const int PausaDopoResa     = 900; // 15 minuti

        private const string ChiaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string NomeRun   = "AeraTray";
        private const string TaskAvvio = "Aera_Segnalatore";

        // L'avvio automatico puo' arrivare da due strade: l'attivita'
        // pianificata creata dall'installatore, che pero' per
        // spegnerla servono i privilegi di amministratore, e la chiave
        // Run dell'utente, che invece si comanda liberamente. Si
        // guardano entrambe e si agisce su quella che si puo'.
        public bool AvvioAutomaticoAttivo()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ChiaveRun, false))
                    if (k != null && k.GetValue(NomeRun) != null) return true;
            }
            catch { }

            return AttivitaAbilitata();
        }

        private static bool AttivitaAbilitata()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe",
                              "/query /tn \"" + TaskAvvio + "\" /fo csv /nh");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process p = Process.Start(psi))
                {
                    string testo = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return false; }
                    if (p.ExitCode != 0) return false;

                    string t = testo.ToLowerInvariant();
                    return !(t.Contains("disabilitat") || t.Contains("disabled"));
                }
            }
            catch { return false; }
        }

        public void CambiaAvvioAutomatico()
        {
            bool acceso = AvvioAutomaticoAttivo();
            bool nuovo = !acceso;
            string guaio = "";

            // La chiave Run non richiede privilegi ed e' sempre
            // scrivibile: e' la strada principale.
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ChiaveRun))
                {
                    if (k != null)
                    {
                        if (nuovo) k.SetValue(NomeRun, "\"" + Application.ExecutablePath + "\"");
                        else if (k.GetValue(NomeRun) != null) k.DeleteValue(NomeRun, false);
                    }
                }
            }
            catch (Exception ex) { guaio = ex.Message; }

            // Si prova anche sull'attivita' pianificata: senza,
            // spegnendo resterebbe lei ad avviarlo lo stesso.
            Attivita(nuovo ? "/enable" : "/disable");

            if (nuovo != AvvioAutomaticoAttivo() && guaio.Length == 0)
                guaio = "l'attivita' pianificata e' comandata dall'amministratore";

            if (voceAvvio != null) voceAvvio.Checked = AvvioAutomaticoAttivo();

            icona.BalloonTipIcon = (guaio.Length > 0) ? ToolTipIcon.Warning : ToolTipIcon.Info;
            icona.BalloonTipTitle = "Avvio automatico";
            icona.BalloonTipText = (guaio.Length > 0)
                ? ("Non del tutto riuscito: " + guaio)
                : (nuovo ? "Il segnalatore si aprira' a ogni accesso."
                         : "Il segnalatore non si aprira' piu' da solo.");
            icona.ShowBalloonTip(5000);
        }

        private static void Attivita(string opzione)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe",
                              "/change /tn \"" + TaskAvvio + "\" " + opzione);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } }
                }
            }
            catch { }
        }

        public Vassoio() : this(false) { }

        public Vassoio(bool apriSubito)
        {
            attivoPrima     = new bool[applicativi.Length];
            ultimoTentativo = new DateTime[applicativi.Length];
            tentativiFalliti = new int[applicativi.Length];
            fermatoAMano    = new bool[applicativi.Length];
            for (int i = 0; i < applicativi.Length; i++)
                ultimoTentativo[i] = DateTime.MinValue;

            RilevaInstallati();
            CaricaSorvegliati();

            var menu = new ContextMenuStrip();
            var intestazione = new ToolStripMenuItem("AeraControl  " + Versione.Numero);
            intestazione.Enabled = false;
            menu.Items.Add(intestazione);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Dettagli...",   null, delegate { ApriDettaglio(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Avvia tutti",   null, delegate { Tutti("run"); });
            menu.Items.Add("Ferma tutti",   null, delegate { Tutti("end"); });
            menu.Items.Add("Riavvia tutti", null, delegate { Riavvia(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Aggiorna",      null, delegate { Controlla(); });

            // Sottomenu per scegliere chi conta nel colore dell'icona
            var sorv = new ToolStripMenuItem("Sorveglia");
            foreach (Applicativo a in applicativi)
            {
                Applicativo app = a;
                var v = new ToolStripMenuItem(a.Titolo);
                v.CheckOnClick = true;
                v.Checked = a.Sorvegliato;
                if (!a.Installato) v.Text = a.Titolo + "  (non installato)";
                v.Click += delegate { CambiaSorveglianza(app.Task, v.Checked); };
                sorv.DropDownItems.Add(v);
            }
            menu.Items.Add(sorv);

            voceAvvio = new ToolStripMenuItem("Avvia all'accesso a Windows");
            voceAvvio.Click += delegate { CambiaAvvioAutomatico(); };
            menu.Items.Add(voceAvvio);

            voceGuardiano = new ToolStripMenuItem("Tieni sempre attivi gli applicativi");
            voceGuardiano.Checked = GuardianoAttivo();
            voceGuardiano.Click += delegate { CambiaGuardiano(); };
            menu.Items.Add(voceGuardiano);

            // Lo stato si rilegge ogni volta che si apre il menu: puo'
            // essere stato cambiato dall'Utilita' di pianificazione.
            menu.Opening += delegate
            {
                voceAvvio.Checked = AvvioAutomaticoAttivo();
                voceGuardiano.Checked = GuardianoAttivo();
            };

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Chiudi il segnalatore", null, delegate { Esci(); });

            icona = new NotifyIcon();
            icona.ContextMenuStrip = menu;
            icona.Visible = true;
            icona.Text = "AeraControl";
            // Un clic solo apre il dettaglio: e' il gesto che ci si
            // aspetta, e il doppio clic resta come scorciatoia.
            icona.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ApriDettaglio();
            };
            icona.BalloonTipClicked += delegate { ApriDettaglio(); };
            AggiornaIcona(0, applicativi.Length);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 5000;
            timer.Tick += delegate { Controlla(); };
            timer.Start();

            Controlla();

            if (apriSubito) ApriDettaglio();
        }

        // ---------------------------------------------- sorveglianza
        // Non tutte le macchine hanno tutto: il proxy Orderman puo'
        // non essere installato. Chi non c'e', o chi si sceglie di non
        // sorvegliare, non deve far diventare gialla la spia.
        private void RilevaInstallati()
        {
            foreach (Applicativo a in applicativi)
            {
                try
                {
                    if (a.EServizio) a.Installato = ServizioEsiste(a.Servizio);
                    else a.Installato = File.Exists(a.Percorso);
                }
                catch { a.Installato = false; }
            }
        }

        private static bool ServizioEsiste(string nome)
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", "query \"" + nome + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return false; }
                    // 1060 = il servizio non esiste
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        private static string FileSorvegliati
        {
            get
            {
                return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),
                                    "sorvegliati.txt");
            }
        }

        private void CaricaSorvegliati()
        {
            // Senza file si parte dal buon senso: si sorveglia cio'
            // che risulta installato.
            if (!File.Exists(FileSorvegliati))
            {
                foreach (Applicativo a in applicativi) a.Sorvegliato = a.Installato;
                return;
            }

            try
            {
                var scelti = new List<string>();
                foreach (string r in File.ReadAllLines(FileSorvegliati, Encoding.UTF8))
                {
                    string s = r.Trim();
                    if (s.Length > 0 && !s.StartsWith("#")) scelti.Add(s);
                }
                foreach (Applicativo a in applicativi)
                    a.Sorvegliato = scelti.Contains(a.Task);
            }
            catch
            {
                foreach (Applicativo a in applicativi) a.Sorvegliato = a.Installato;
            }
        }

        private void SalvaSorvegliati()
        {
            try
            {
                var righe = new List<string>();
                righe.Add("# applicativi che contribuiscono al colore dell'icona");
                foreach (Applicativo a in applicativi)
                    if (a.Sorvegliato) righe.Add(a.Task);
                File.WriteAllLines(FileSorvegliati, righe.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        public void CambiaSorveglianza(string task, bool sorveglia)
        {
            foreach (Applicativo a in applicativi)
                if (a.Task == task) a.Sorvegliato = sorveglia;
            SalvaSorvegliati();
            Controlla();
        }

        // ---------------------------------------------- il guardiano
        private static string FileGuardiano
        {
            get
            {
                return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),
                                    "guardiano.txt");
            }
        }

        // Acceso di serie: su un server che si riavvia da solo, il caso
        // normale e' volere gli applicativi su. Chi non lo vuole lo
        // spegne, e allora il file esiste e dice 0.
        public bool GuardianoAttivo()
        {
            try
            {
                if (!File.Exists(FileGuardiano)) return true;
                foreach (string r in File.ReadAllLines(FileGuardiano, Encoding.UTF8))
                {
                    string s = r.Trim();
                    if (s.Length == 0 || s.StartsWith("#")) continue;
                    return s == "1";
                }
            }
            catch { }
            return true;
        }

        public void CambiaGuardiano()
        {
            bool nuovo = !GuardianoAttivo();
            try
            {
                var righe = new string[]
                {
                    "# 1 = il segnalatore riavvia gli applicativi sorvegliati che trova fermi",
                    "# 0 = si limita a guardare",
                    nuovo ? "1" : "0"
                };
                File.WriteAllLines(FileGuardiano, righe, Encoding.UTF8);
            }
            catch { }

            // Riaccendendolo si azzerano le rese precedenti: e' un
            // ripensamento, non un altro giro dello stesso fallimento.
            if (nuovo)
                for (int i = 0; i < applicativi.Length; i++)
                {
                    tentativiFalliti[i] = 0;
                    fermatoAMano[i] = false;
                    ultimoTentativo[i] = DateTime.MinValue;
                }

            if (voceGuardiano != null) voceGuardiano.Checked = GuardianoAttivo();
            Annota(nuovo ? "guardiano acceso" : "guardiano spento");

            icona.BalloonTipIcon = ToolTipIcon.Info;
            icona.BalloonTipTitle = "Avvio automatico degli applicativi";
            icona.BalloonTipText = nuovo
                ? "Gli applicativi sorvegliati che risultano fermi vengono riavviati."
                : "Il segnalatore si limita a guardare, non avvia piu' niente.";
            icona.ShowBalloonTip(5000);
        }

        // Chiamato a ogni giro di controllo, quando lo stato di tutti
        // e' appena stato letto.
        private void Guardiano()
        {
            if (!GuardianoAttivo() || avvioInCorso) return;

            // All'accesso a Windows la macchina e' ancora occupata e
            // qualcosa puo' partire per conto suo: prima si lascia
            // sistemare, poi si guarda che cosa manca davvero.
            if ((DateTime.UtcNow - acceso).TotalSeconds < AttesaIniziale) return;

            var mancanti = new List<int>();
            for (int i = 0; i < applicativi.Length; i++)
            {
                if (!applicativi[i].Sorvegliato) continue;
                if (!applicativi[i].Installato) continue;
                if (fermatoAMano[i]) continue;
                if (attivoPrima[i]) continue;

                double da = (DateTime.UtcNow - ultimoTentativo[i]).TotalSeconds;
                double pausa = (tentativiFalliti[i] >= TentativiMax)
                             ? PausaDopoResa : PausaFraTentativi;
                if (da < pausa) continue;

                mancanti.Add(i);
            }

            if (mancanti.Count == 0) return;

            // L'ordine e' quello dell'elenco, che tiene il proxy
            // Orderman per ultimo: deve trovare gli altri gia' su.
            avvioInCorso = true;
            var t = new Thread(delegate()
            {
                var riusciti = new List<string>();
                var arresi = new List<string>();

                foreach (int i in mancanti)
                {
                    Applicativo a = applicativi[i];
                    ultimoTentativo[i] = DateTime.UtcNow;
                    Annota("fermo: " + a.Titolo + " - avvio (tentativo " +
                           (tentativiFalliti[i] + 1) + " di " + TentativiMax + ")");

                    Comanda("run", a);

                    if (AttendiAvvio(a, 15))
                    {
                        tentativiFalliti[i] = 0;
                        riusciti.Add(a.Titolo);
                        Annota("  partito");
                    }
                    else
                    {
                        tentativiFalliti[i]++;
                        Annota("  non e' partito");
                        if (tentativiFalliti[i] >= TentativiMax)
                        {
                            arresi.Add(a.Titolo);
                            Annota("  mi fermo: riprovo fra " +
                                   (PausaDopoResa / 60) + " minuti");
                        }
                    }
                    Thread.Sleep(1200);
                }

                avvioInCorso = false;
                Invoca(delegate
                {
                    Controlla();
                    AggiornaDettaglio();

                    if (arresi.Count > 0)
                    {
                        icona.BalloonTipIcon = ToolTipIcon.Warning;
                        icona.BalloonTipTitle = "Non riesco ad avviarli";
                        icona.BalloonTipText = string.Join(", ", arresi.ToArray()) +
                            " - riprovo fra " + (PausaDopoResa / 60) + " minuti";
                        icona.ShowBalloonTip(9000);
                    }
                    else if (riusciti.Count > 0)
                    {
                        icona.BalloonTipIcon = ToolTipIcon.Info;
                        icona.BalloonTipTitle = "Applicativi riavviati";
                        icona.BalloonTipText = string.Join(", ", riusciti.ToArray());
                        icona.ShowBalloonTip(6000);
                    }
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        // Un'attivita' pianificata non e' istantanea: si concede il
        // tempo di comparire prima di dichiararla fallita.
        private static bool AttendiAvvio(Applicativo a, int secondi)
        {
            for (int i = 0; i < secondi * 2; i++)
            {
                Thread.Sleep(500);
                if (InEsecuzione(a)) return true;
            }
            return false;
        }

        // Registro di cosa ha fatto il guardiano: senza, dopo un
        // riavvio notturno non resterebbe traccia di niente.
        private static void Annota(string testo)
        {
            try
            {
                string f = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath), "guardiano.log");

                try
                {
                    var info = new FileInfo(f);
                    if (info.Exists && info.Length > 200000) File.Delete(f);
                }
                catch { }

                File.AppendAllText(f,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + testo +
                    Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        // ------------------------------------------------- dettaglio
        private void ApriDettaglio()
        {
            try
            {
                if (dettaglio == null || dettaglio.IsDisposed)
                {
                    dettaglio = new FormDettaglio(applicativi,
                        delegate(string verbo, string task)
                        {
                            if (task == null) Tutti(verbo);
                            else Uno(verbo, task);
                        },
                        iconaCorrente,
                        delegate { return AvvioAutomaticoAttivo(); },
                        delegate { CambiaAvvioAutomatico(); },
                        delegate(string task, bool su) { CambiaSorveglianza(task, su); },
                        delegate { return GuardianoAttivo(); },
                        delegate { CambiaGuardiano(); });
                }

                if (dettaglio.Visible) { dettaglio.Hide(); return; }
                dettaglio.MostraVicinoAllOrologio();
            }
            catch { }
        }

        private void Uno(string verbo, string task)
        {
            var t = new Thread(delegate()
            {
                for (int i = 0; i < applicativi.Length; i++)
                    if (applicativi[i].Task == task) { Comanda(verbo, applicativi[i]); Segna(verbo, i); break; }

                Thread.Sleep(2000);
                Invoca(delegate { Controlla(); AggiornaDettaglio(); });
            });
            t.IsBackground = true;
            t.Start();
        }

        // Chi ferma un applicativo da qui lo vuole fermo: il guardiano
        // lo lascia stare finche' non lo si riavvia, altrimenti il
        // pulsante Ferma non servirebbe a niente.
        private void Segna(string verbo, int i)
        {
            if (verbo == "end") { fermatoAMano[i] = true; Annota("fermato a mano: " + applicativi[i].Titolo); }
            else { fermatoAMano[i] = false; tentativiFalliti[i] = 0; }
        }

        // -------------------------------------------------- controllo
        private void Controlla()
        {
            int attivi = 0;
            int contati = 0;
            var righe = new StringBuilder();
            var caduti = new List<string>();

            for (int i = 0; i < applicativi.Length; i++)
            {
                bool attivo = InEsecuzione(applicativi[i]);

                // Solo i sorvegliati pesano sul colore dell'icona
                if (applicativi[i].Sorvegliato)
                {
                    contati++;
                    if (attivo) attivi++;
                }

                // Segnala solo chi era su e non c'e' piu': all'avvio
                // del segnalatore non ha senso avvisare di nulla.
                if (!primoGiro && applicativi[i].Sorvegliato &&
                    attivoPrima[i] && !attivo)
                    caduti.Add(applicativi[i].Titolo);

                attivoPrima[i] = attivo;

                if (!applicativi[i].Sorvegliato) continue;
                righe.Append(attivo ? "OK  " : "--  ");
                righe.Append(applicativi[i].Titolo);
                righe.Append("\n");
            }

            primoGiro = false;

            ScriviStato();
            AggiornaIcona(attivi, contati);

            string intestazione;
            if (contati == 0)
                intestazione = "AeraControl: nessuno sorvegliato";
            else if (attivi == contati)
                intestazione = "AeraControl: tutti attivi";
            else
                intestazione = string.Format("AeraControl: {0} di {1} attivi",
                                             attivi, contati);

            // Il fumetto di sistema tronca oltre i 63 caratteri
            string testo = intestazione + "\n" + righe.ToString();
            if (testo.Length > 62) testo = testo.Substring(0, 62);
            icona.Text = testo;

            // Con il guardiano acceso l'avviso lo da' lui, dicendo
            // anche com'e' andata: qui si tacerebbe due volte la stessa
            // cosa.
            if (caduti.Count > 0 && !GuardianoAttivo())
            {
                icona.BalloonTipIcon = ToolTipIcon.Warning;
                icona.BalloonTipTitle = (caduti.Count == 1)
                    ? "Un applicativo si e' chiuso"
                    : "Alcuni applicativi si sono chiusi";
                icona.BalloonTipText = string.Join(", ", caduti.ToArray());
                icona.ShowBalloonTip(8000);
            }

            Guardiano();
        }

        private static bool InEsecuzione(Applicativo a)
        {
            if (a.EServizio) return ServizioAttivo(a.Servizio);
            try { return Process.GetProcessesByName(a.Processo).Length > 0; }
            catch { return false; }
        }

        // Lo stato di un servizio lo dice il gestore dei servizi: il
        // processo puo' esserci anche mentre il servizio si sta
        // fermando, e viceversa.
        private static bool ServizioAttivo(string nome)
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", "query \"" + nome + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process p = Process.Start(psi))
                {
                    string testo = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return false; }
                    if (p.ExitCode != 0) return false;
                    // RUNNING non viene tradotto nemmeno in italiano
                    return testo.ToUpperInvariant().Contains("RUNNING");
                }
            }
            catch { return false; }
        }

        // Lo stato vero dei processi, scritto su file perche' possa
        // leggerlo anche il client.
        //
        // Serve perche' schtasks dice se e' in corso l'ATTIVITA', non
        // se il programma sta girando: basta che l'applicativo lasci
        // vivo un processo figlio e l'attivita' risulta ancora in
        // esecuzione anche dopo che il programma e' stato chiuso. Qui
        // invece si guardano i processi, che e' quello che conta.
        private void ScriviStato()
        {
            try
            {
                string cartella = Path.GetDirectoryName(Application.ExecutablePath);
                string file = Path.Combine(cartella, "stato.txt");

                var sb = new StringBuilder();
                sb.AppendLine("# stato degli applicativi Aera, scritto da AeraTray");
                sb.AppendLine("# task|attivo|pid|memoriaMB");

                foreach (Applicativo a in applicativi)
                {
                    if (a.EServizio)
                    {
                        // Per un servizio conta lo stato dichiarato dal
                        // gestore; PID e memoria si prendono dal
                        // processo se c'e'.
                        bool su = ServizioAttivo(a.Servizio);
                        Process[] ps = null;
                        try { ps = Process.GetProcessesByName(a.Processo); }
                        catch { }

                        if (su && ps != null && ps.Length > 0)
                        {
                            double m = 0;
                            try { m = Math.Round(ps[0].WorkingSet64 / 1048576.0, 1); }
                            catch { }
                            sb.AppendLine(string.Format(
                                System.Globalization.CultureInfo.InvariantCulture,
                                "{0}|1|{1}|{2}", a.Task, ps[0].Id, m));
                        }
                        else sb.AppendLine(a.Task + (su ? "|1||" : "|0||"));
                        continue;
                    }

                    Process[] p = null;
                    try { p = Process.GetProcessesByName(a.Processo); }
                    catch { }

                    if (p != null && p.Length > 0)
                    {
                        double mb = 0;
                        try { mb = Math.Round(p[0].WorkingSet64 / 1048576.0, 1); }
                        catch { }
                        sb.AppendLine(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0}|1|{1}|{2}", a.Task, p[0].Id, mb));
                    }
                    else sb.AppendLine(a.Task + "|0||");
                }

                // Scrittura su file temporaneo e sostituzione: il
                // client non deve mai trovare un file a meta'.
                string tmp = file + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
            catch { }
        }

        // ----------------------------------------------------- icona
        private void AggiornaIcona(int attivi, int totale)
        {
            Color tinta;
            // Se non si sorveglia niente non c'e' nulla da segnalare:
            // verde, non rosso.
            if (totale == 0)           tinta = Color.FromArgb(38, 160, 92);
            else if (attivi == 0)      tinta = Color.FromArgb(200, 62, 55);
            else if (attivi < totale)  tinta = Color.FromArgb(224, 158, 30);
            else                       tinta = Color.FromArgb(38, 160, 92);

            Icon nuova = CostruisciIcona(tinta);
            Icon vecchia = iconaCorrente;

            iconaCorrente = nuova;
            icona.Icon = nuova;

            if (vecchia != null) vecchia.Dispose();
        }

        // Bitmap.GetHicon() perde il canale alfa: il pallino usciva
        // come un quadrato pieno con dentro un anello. Si costruisce
        // quindi un vero file ICO in memoria, che l'alfa lo conserva,
        // con due misure cosi' Windows sceglie quella giusta senza
        // rimpicciolire a occhio.
        private static Icon CostruisciIcona(Color tinta)
        {
            int[] misure = new int[] { 16, 32 };
            var pezzi = new List<byte[]>();
            var lati = new List<int>();

            foreach (int m in misure)
            {
                using (Bitmap b = DisegnaPallino(tinta, m))
                {
                    pezzi.Add(DatiImmagine(b));
                    lati.Add(m);
                }
            }

            byte[] tutto;
            using (var ms = new MemoryStream())
            {
                var w = new BinaryWriter(ms);
                w.Write((short)0);              // riservato
                w.Write((short)1);              // 1 = icona
                w.Write((short)pezzi.Count);

                int scostamento = 6 + 16 * pezzi.Count;
                for (int i = 0; i < pezzi.Count; i++)
                {
                    w.Write((byte)lati[i]);     // larghezza
                    w.Write((byte)lati[i]);     // altezza
                    w.Write((byte)0);           // colori tavolozza
                    w.Write((byte)0);           // riservato
                    w.Write((short)1);          // piani
                    w.Write((short)32);         // bit per pixel
                    w.Write(pezzi[i].Length);
                    w.Write(scostamento);
                    scostamento += pezzi[i].Length;
                }
                foreach (byte[] p in pezzi) w.Write(p);
                w.Flush();
                tutto = ms.ToArray();
            }

            using (var ms = new MemoryStream(tutto))
                return new Icon(ms, SystemInformation.SmallIconSize);
        }

        // Intestazione BITMAPINFOHEADER + pixel BGRA dal basso verso
        // l'alto + maschera vuota: con 32 bit conta solo l'alfa.
        private static byte[] DatiImmagine(Bitmap b)
        {
            int larg = b.Width, alt = b.Height;
            int rigaMaschera = ((larg + 31) / 32) * 4;
            int dimPixel = larg * alt * 4;
            int dimMaschera = rigaMaschera * alt;

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);

            w.Write(40);
            w.Write(larg);
            w.Write(alt * 2);          // colore + maschera
            w.Write((short)1);
            w.Write((short)32);
            w.Write(0);
            w.Write(dimPixel + dimMaschera);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0);

            BitmapData bd = b.LockBits(new Rectangle(0, 0, larg, alt),
                                       ImageLockMode.ReadOnly,
                                       PixelFormat.Format32bppArgb);
            try
            {
                byte[] riga = new byte[larg * 4];
                for (int y = alt - 1; y >= 0; y--)
                {
                    IntPtr p = new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride);
                    Marshal.Copy(p, riga, 0, riga.Length);
                    w.Write(riga, 0, riga.Length);
                }
            }
            finally { b.UnlockBits(bd); }

            w.Write(new byte[dimMaschera]);
            w.Flush();
            return ms.ToArray();
        }

        private static Bitmap DisegnaPallino(Color tinta, int lato)
        {
            var bmp = new Bitmap(lato, lato, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(0, 0, 0, 0));

                float m = Math.Max(1f, lato / 16f);
                var r = new RectangleF(m, m, lato - m * 2, lato - m * 2);

                using (var p = new SolidBrush(tinta)) g.FillEllipse(p, r);

                // Un contorno scuro stacca il pallino sia dalle barre
                // chiare sia da quelle scure.
                using (var p = new Pen(Color.FromArgb(90, 0, 0, 0), Math.Max(1f, lato / 22f)))
                    g.DrawEllipse(p, r);

                using (var p = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                    g.FillEllipse(p, r.X + r.Width * 0.24f, r.Y + r.Height * 0.16f,
                                     r.Width * 0.52f, r.Height * 0.30f);
            }
            return bmp;
        }

        // ---------------------------------------------------- comandi
        private void Tutti(string verbo)
        {
            var t = new Thread(delegate()
            {
                for (int i = 0; i < applicativi.Length; i++)
                {
                    Comanda(verbo, applicativi[i]);
                    Segna(verbo, i);
                    Thread.Sleep(800);
                }
                Thread.Sleep(1500);
                Invoca(delegate { Controlla(); AggiornaDettaglio(); });
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Riavvia()
        {
            var t = new Thread(delegate()
            {
                // Il guardiano non deve intromettersi fra l'arresto e
                // il riavvio, o riaccenderebbe cio' che si sta
                // fermando apposta.
                avvioInCorso = true;
                foreach (Applicativo a in applicativi) Comanda("end", a);
                Thread.Sleep(4000);
                for (int i = 0; i < applicativi.Length; i++)
                {
                    Comanda("run", applicativi[i]);
                    Segna("run", i);
                    Thread.Sleep(1000);
                }
                Thread.Sleep(2000);
                avvioInCorso = false;
                Invoca(delegate { Controlla(); AggiornaDettaglio(); });
            });
            t.IsBackground = true;
            t.Start();
        }

        // "run" e "end" sono i verbi interni: per i servizi diventano
        // start e stop di sc.
        private void Comanda(string verbo, Applicativo a)
        {
            if (a.EServizio) Sc((verbo == "end") ? "stop" : "start", a.Servizio);
            else Schtasks(verbo, a.Task);
        }

        private static void Sc(string verbo, string nome)
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", verbo + " \"" + nome + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } }
                }
            }
            catch { }
        }

        private void AggiornaDettaglio()
        {
            if (dettaglio != null && !dettaglio.IsDisposed && dettaglio.Visible)
                dettaglio.Aggiorna();
        }

        // In locale non servono credenziali: si agisce nella propria
        // sessione, sulle stesse attivita' che comanda il client.
        private static void Schtasks(string verbo, string task)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe",
                              string.Format("/{0} /tn \"{1}\"", verbo, task));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(20000);
                }
            }
            catch { }
        }

        private void Invoca(MethodInvoker azione)
        {
            try
            {
                if (icona != null && icona.ContextMenuStrip != null &&
                    icona.ContextMenuStrip.InvokeRequired)
                    icona.ContextMenuStrip.BeginInvoke(azione);
                else
                    azione();
            }
            catch { }
        }

        private void Esci()
        {
            timer.Stop();
            icona.Visible = false;
            icona.Dispose();
            if (iconaCorrente != null) iconaCorrente.Dispose();
            ExitThread();
        }
    }

    static class Programma
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] argomenti)
        {
            bool apriSubito = false;
            foreach (string a in argomenti)
                if (string.Equals(a, "/dettagli", StringComparison.OrdinalIgnoreCase))
                    apriSubito = true;

            try
            {
                if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();
            }
            catch { }

            // Una sola icona per sessione: l'attivita' pianificata puo'
            // scattare piu' volte e si finirebbe con due pallini uguali.
            bool nuovo;
            using (var solo = new Mutex(true, "AeraTray_" + Environment.UserName, out nuovo))
            {
                if (!nuovo) return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Vassoio(apriSubito));
            }
        }
    }
}
