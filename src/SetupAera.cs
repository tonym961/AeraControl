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
    // Numero unico del prodotto: lo stesso di AeraControl.cs e di
    // AeraTray.cs. La nota estesa sta in AeraControl.cs.
    public static class Versione
    {
        public const string Numero = "1.6.6";
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
        public bool Centrato = false;

        // Il pulsante di conferma cambia colore secondo il ruolo scelto
        public Color Tinta
        {
            get { return tinta; }
            set { tinta = value; Invalidate(); }
        }

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

            Rectangle rt = Centrato
                ? new Rectangle(8, 0, Width - 16, Height)
                : new Rectangle(16, 0, Width - 24, Height);

            TextFormatFlags dove = Centrato
                ? (TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter)
                : (TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                   TextFormatFlags.WordBreak);

            TextRenderer.DrawText(g, Text, Font, rt, testo, dove);
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

    // Una delle due scelte di ruolo: disegno, titolo, spiegazione.
    // Si accende quando la si sceglie, e l'altra si spegne.
    public class Scheda : Control
    {
        public Color Tinta = Stile.Blu;
        public string Titolo = "";
        public string Descrizione = "";
        public bool Server = false;
        private bool scelta, sotto;

        public bool Scelta
        {
            get { return scelta; }
            set { scelta = value; Invalidate(); }
        }

        public Scheda()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { sotto = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { sotto = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Color dietro = (Parent != null) ? Parent.BackColor : Stile.Sfondo;
            using (var f = new SolidBrush(dietro)) g.FillRectangle(f, ClientRectangle);

            // Il bordo scelto e' spesso il doppio: si disegna un pixel
            // piu' dentro, altrimenti la meta' esterna viene tagliata.
            int spessore = scelta ? 2 : 1;
            var r = new Rectangle(spessore - 1, spessore - 1,
                                  Width - spessore * 2 + 1, Height - spessore * 2 + 1);

            Color bordo = scelta ? Tinta
                        : (sotto && Enabled ? Stile.Mescola(Stile.Bordo, Tinta, 0.55f)
                                            : Stile.Bordo);
            Color fondo = Enabled
                ? (scelta ? Stile.Schiarisci(Tinta, 0.94f)
                          : (sotto ? Color.FromArgb(250, 251, 252) : Color.White))
                : Color.FromArgb(248, 249, 250);

            using (GraphicsPath gp = Stile.Tondo(r, 10))
            {
                using (var f = new SolidBrush(fondo)) g.FillPath(f, gp);
                using (var pen = new Pen(fondo, 1.4f)) g.DrawPath(pen, gp);
                using (var pen = new Pen(bordo, spessore)) g.DrawPath(pen, gp);
            }

            Color acceso = Enabled ? Tinta : Stile.Mescola(Tinta, Color.White, 0.55f);
            Disegno(g, new Rectangle((Width - 30) / 2, 16, 30, 30), acceso);

            Color ct = Enabled ? Stile.Testo : Color.FromArgb(170, 178, 188);
            Color cd = Enabled ? Stile.TestoTenue : Color.FromArgb(185, 192, 200);

            using (var f = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                TextRenderer.DrawText(g, Titolo, f, new Rectangle(6, 54, Width - 12, 22), ct,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);

            using (var f = new Font("Segoe UI", 8.5F))
                TextRenderer.DrawText(g, Descrizione, f, new Rectangle(14, 78, Width - 28, 40), cd,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top |
                    TextFormatFlags.WordBreak);
        }

        // Disegnati qui a mano: in WinForms non esistono icone pronte, e
        // incorporare immagini per due simboli non vale il peso.
        private void Disegno(Graphics g, Rectangle r, Color c)
        {
            using (var b = new SolidBrush(c))
            using (var pallino = new SolidBrush(Color.White))
            {
                if (Server)
                {
                    // Tre moduli impilati, come un armadio rack
                    for (int i = 0; i < 3; i++)
                    {
                        var m = new Rectangle(r.X + 2, r.Y + 1 + i * 10, r.Width - 4, 8);
                        using (GraphicsPath gp = Stile.Tondo(m, 2)) g.FillPath(b, gp);
                        g.FillEllipse(pallino, m.X + 3, m.Y + 3, 3, 3);
                    }
                }
                else
                {
                    // Uno schermo con la sua base
                    var s = new Rectangle(r.X + 1, r.Y + 3, r.Width - 2, 18);
                    using (GraphicsPath gp = Stile.Tondo(s, 3)) g.FillPath(b, gp);
                    var d = new Rectangle(s.X + 3, s.Y + 3, s.Width - 6, 12);
                    using (GraphicsPath gp = Stile.Tondo(d, 1)) g.FillPath(pallino, gp);
                    var p = new Rectangle(r.X + 10, r.Y + 21, r.Width - 20, 3);
                    g.FillRectangle(b, p);
                    var q = new Rectangle(r.X + 4, r.Y + 24, r.Width - 8, 3);
                    using (GraphicsPath gp = Stile.Tondo(q, 1)) g.FillPath(b, gp);
                }
            }
        }
    }

    // Emblema in testata. Non si usa icona.ico: e' il logotipo
    // "iotatau", largo, e dentro un quadrato di 34 px si schiaccia
    // fino a diventare una macchia grigia illeggibile. Qui si disegna
    // lo stesso motivo delle schede, che a questa misura si legge.
    public class Emblema : Control
    {
        public Emblema()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Color dietro = (Parent != null) ? Parent.BackColor : Color.White;
            using (var f = new SolidBrush(dietro)) g.FillRectangle(f, ClientRectangle);

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath gp = Stile.Tondo(r, 9))
            using (var f = new SolidBrush(Stile.Testata))
            {
                g.FillPath(f, gp);
                using (var pen = new Pen(Stile.Testata, 1.4f)) g.DrawPath(pen, gp);
            }

            using (var b = new SolidBrush(Color.White))
            using (var punto = new SolidBrush(Stile.Testata))
            {
                for (int i = 0; i < 2; i++)
                {
                    var m = new Rectangle(8, 9 + i * 9, Width - 16, 7);
                    using (GraphicsPath gp = Stile.Tondo(m, 2)) g.FillPath(b, gp);
                    g.FillEllipse(punto, m.X + 3, m.Y + 2, 3, 3);
                }
            }
        }
    }

    // Striscia che apre e chiude il registro: da chiusa la finestra
    // resta corta, perche' finche' non si sceglie non c'e' niente da
    // leggere.
    public class BarraRegistro : Control
    {
        public bool Aperto = false;
        private bool sotto;

        public BarraRegistro()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { sotto = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { sotto = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Color dietro = (Parent != null) ? Parent.BackColor : Stile.Sfondo;
            using (var f = new SolidBrush(dietro)) g.FillRectangle(f, ClientRectangle);

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fondo = sotto ? Color.FromArgb(234, 238, 242) : Color.FromArgb(238, 241, 244);
            using (GraphicsPath gp = Stile.Tondo(r, 8))
            {
                using (var f = new SolidBrush(fondo)) g.FillPath(f, gp);
                using (var pen = new Pen(fondo, 1.4f)) g.DrawPath(pen, gp);
                using (var pen = new Pen(Stile.Bordo)) g.DrawPath(pen, gp);
            }

            TextRenderer.DrawText(g, Text, Font, new Rectangle(14, 0, Width - 50, Height),
                Stile.TestoTenue, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // Freccia: in giu' quando e' chiuso, in su' quando e' aperto
            int cx = Width - 22, cy = Height / 2;
            Point[] punte = Aperto
                ? new Point[] { new Point(cx - 5, cy + 2), new Point(cx + 5, cy + 2), new Point(cx, cy - 3) }
                : new Point[] { new Point(cx - 5, cy - 2), new Point(cx + 5, cy - 2), new Point(cx, cy + 3) };
            using (var b = new SolidBrush(Color.FromArgb(139, 149, 163))) g.FillPolygon(b, punte);
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
        private Scheda schServer, schClient;
        private PulsanteTondo btnInstalla, btnRimuovi, btnChiudi;
        private BarraRegistro barra;
        private Riquadro cornice, opzioni;
        private Label lblStato, firma;

        // Le due domande che il PowerShell del server faceva a riga di
        // comando. Chiederle qui vuol dire poterlo far girare in
        // silenzio, senza aprire una seconda finestra nera.
        private TextBox txtUtente;
        private CheckBox chkSegnalatore;

        private int ruolo;              // 0 nessuno, 1 server, 2 client
        private bool registroAperto;

        // Senza privilegi non si installa: non si avvisa soltanto, si
        // impedisce. Cosi' non si arriva a meta' lavoro per poi
        // fallire sulla prima chiave di registro.
        private bool bloccato;

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
            IconaDaRisorsa();

            var testata = new Panel();
            testata.Location = new Point(0, 0);
            testata.Size = new Size(Largo, 76);
            testata.BackColor = Color.White;
            Controls.Add(testata);

            var riga = new Panel();
            riga.Location = new Point(0, 75);
            riga.Size = new Size(Largo, 1);
            riga.BackColor = Stile.Bordo;
            testata.Controls.Add(riga);

            var marchio = new Emblema();
            marchio.Location = new Point(20, 21);
            marchio.Size = new Size(34, 34);
            testata.Controls.Add(marchio);

            var titolo = new Label();
            titolo.Text = "AeraControl";
            titolo.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            titolo.ForeColor = Stile.Testo;
            titolo.BackColor = Color.Transparent;
            titolo.Location = new Point(64, 18);
            titolo.Size = new Size(340, 28);
            testata.Controls.Add(titolo);

            var sotto = new Label();
            sotto.Text = "installazione - versione " + Versione.Numero;
            sotto.ForeColor = Stile.TestoTenue;
            sotto.BackColor = Color.Transparent;
            sotto.Font = new Font("Segoe UI", 8.5F);
            sotto.Location = new Point(65, 45);
            sotto.Size = new Size(400, 18);
            testata.Controls.Add(sotto);

            lblStato = new Label();
            lblStato.TextAlign = ContentAlignment.MiddleRight;
            lblStato.ForeColor = Stile.TestoTenue;
            lblStato.BackColor = Color.Transparent;
            lblStato.Font = new Font("Segoe UI", 8.5F);
            lblStato.Location = new Point(Largo - 20 - 280, 30);
            lblStato.Size = new Size(280, 18);
            testata.Controls.Add(lblStato);

            var domanda = new Label();
            domanda.Text = "Che ruolo ha questo computer?";
            domanda.Font = new Font("Segoe UI", 8.5F);
            domanda.ForeColor = Stile.TestoTenue;
            domanda.Location = new Point(22, 92);
            domanda.Size = new Size(400, 18);
            Controls.Add(domanda);

            int largaScheda = (Largo - 40 - 12) / 2;

            schServer = new Scheda();
            schServer.Server = true;
            schServer.Tinta = Stile.Verde;
            schServer.Titolo = "Server";
            schServer.Descrizione = "Qui girano gli applicativi e compaiono le finestre";
            schServer.Location = new Point(20, 114);
            schServer.Size = new Size(largaScheda, 122);
            schServer.Click += delegate { Scegli(1); };
            Controls.Add(schServer);

            schClient = new Scheda();
            schClient.Tinta = Stile.Blu;
            schClient.Titolo = "Client";
            schClient.Descrizione = "Da qui si comandano gli applicativi che stanno sul server";
            schClient.Location = new Point(20 + largaScheda + 12, 114);
            schClient.Size = new Size(largaScheda, 122);
            schClient.Click += delegate { Scegli(2); };
            Controls.Add(schClient);

            // Compaiono solo scegliendo Server: sul client non
            // servono, e una finestra che mostra impostazioni che non
            // riguardano la scelta fatta confonde e basta.
            opzioni = new Riquadro();
            opzioni.Visible = false;
            Controls.Add(opzioni);

            var etUtente = new Label();
            etUtente.Text = "Utente proprietario delle attivita' pianificate";
            etUtente.Font = new Font("Segoe UI", 8.5F);
            etUtente.ForeColor = Stile.TestoTenue;
            etUtente.BackColor = Color.Transparent;
            etUtente.Location = new Point(14, 12);
            etUtente.Size = new Size(400, 16);
            opzioni.Controls.Add(etUtente);

            txtUtente = new TextBox();
            txtUtente.Text = Environment.MachineName + "\\Administrator";
            txtUtente.Font = new Font("Segoe UI", 9F);
            txtUtente.Location = new Point(14, 32);
            txtUtente.Size = new Size(300, 24);
            opzioni.Controls.Add(txtUtente);

            var notaUtente = new Label();
            notaUtente.Text = "le finestre compaiono nella sua sessione";
            notaUtente.Font = new Font("Segoe UI", 8F);
            notaUtente.ForeColor = Stile.TestoTenue;
            notaUtente.BackColor = Color.Transparent;
            notaUtente.Location = new Point(322, 36);
            notaUtente.Size = new Size(250, 16);
            opzioni.Controls.Add(notaUtente);

            chkSegnalatore = new CheckBox();
            chkSegnalatore.Text = "Apri il segnalatore a ogni accesso a Windows";
            chkSegnalatore.Checked = true;
            chkSegnalatore.Font = new Font("Segoe UI", 9F);
            chkSegnalatore.ForeColor = Stile.Testo;
            chkSegnalatore.BackColor = Color.Transparent;
            chkSegnalatore.Location = new Point(12, 64);
            chkSegnalatore.Size = new Size(400, 22);
            opzioni.Controls.Add(chkSegnalatore);

            barra = new BarraRegistro();
            barra.Text = "Registro";
            barra.Font = new Font("Segoe UI", 8.5F);
            barra.Click += delegate { ApriRegistro(!registroAperto); };
            Controls.Add(barra);

            cornice = new Riquadro();
            Controls.Add(cornice);

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.BackColor = Color.White;
            txtLog.ForeColor = Color.FromArgb(60, 72, 86);
            txtLog.Font = new Font("Consolas", 8.5F);
            cornice.Controls.Add(txtLog);

            btnInstalla = new PulsanteTondo(Stile.Blu);
            btnInstalla.Centrato = true;
            btnInstalla.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnInstalla.Click += delegate { Installa(); };
            Controls.Add(btnInstalla);

            btnRimuovi = new PulsanteTondo(Color.White);
            btnRimuovi.Contorno = true;
            btnRimuovi.Centrato = true;
            btnRimuovi.Text = "Rimuovi";
            btnRimuovi.Size = new Size(110, 36);
            btnRimuovi.Click += delegate { ApriRegistro(true); InBackground(Rimuovi); };
            Controls.Add(btnRimuovi);

            btnChiudi = new PulsanteTondo(Color.White);
            btnChiudi.Contorno = true;
            btnChiudi.Centrato = true;
            btnChiudi.Text = "Chiudi";
            btnChiudi.Size = new Size(110, 36);
            btnChiudi.Click += delegate { Close(); };
            Controls.Add(btnChiudi);

            firma = new Label();
            firma.Text = "IOTATEC srl";
            firma.Font = new Font("Segoe UI", 8F);
            firma.ForeColor = Stile.TestoTenue;
            firma.TextAlign = ContentAlignment.MiddleCenter;
            firma.Size = new Size(200, 16);
            Controls.Add(firma);

            Aggiorna();
            Disponi();

            Shown += delegate { Presentazione(); };
        }

        private void Scegli(int quale)
        {
            if (bloccato || !schServer.Enabled) return;
            ruolo = quale;
            Aggiorna();
            Disponi();
        }

        private void Aggiorna()
        {
            schServer.Scelta = (ruolo == 1);
            schClient.Scelta = (ruolo == 2);

            if (bloccato)
            {
                btnInstalla.Tinta = Stile.Rosso;
                btnInstalla.Text = "Servono i privilegi di amministratore";
                btnInstalla.Enabled = false;
                return;
            }

            if (ruolo == 1)
            {
                btnInstalla.Tinta = Stile.Verde;
                btnInstalla.Text = "Installa come server";
            }
            else if (ruolo == 2)
            {
                btnInstalla.Tinta = Stile.Blu;
                btnInstalla.Text = "Installa come client";
            }
            else
            {
                btnInstalla.Tinta = Stile.Blu;
                btnInstalla.Text = "Scegliere prima il ruolo";
            }
            btnInstalla.Enabled = (ruolo != 0) && schServer.Enabled;
        }

        private string utenteScelto = "";
        private bool segnalatoreScelto = true;

        private void Installa()
        {
            if (ruolo == 0) return;

            // Si leggono adesso, finche' si e' sul filo della finestra:
            // il lavoro vero gira altrove e da li' non si toccano.
            utenteScelto = txtUtente.Text.Trim();
            segnalatoreScelto = chkSegnalatore.Checked;

            ApriRegistro(true);
            if (ruolo == 1) InBackground(InstallaServer);
            else            InBackground(InstallaClient);
        }

        // Che cosa c'e' gia' installato qui. Serve a dire se questa e'
        // una prima installazione o un aggiornamento, e da quale
        // versione: rieseguire il setup su una macchina vecchia deve
        // portarla alla corrente, non lasciarla a meta'.
        private static string VersioneInstallata()
        {
            foreach (string nome in new string[] { "AeraControl.exe", "AeraTray.exe" })
            {
                try
                {
                    string f = Path.Combine(Cartella, nome);
                    if (!File.Exists(f)) continue;
                    var v = FileVersionInfo.GetVersionInfo(f);
                    if (v == null) continue;
                    string s = (v.FileVersion ?? "").Trim();
                    if (s.Length == 0) continue;
                    // csc scrive quattro cifre: si mostra come il resto
                    if (s.EndsWith(".0") && s.Split('.').Length == 4)
                        s = s.Substring(0, s.Length - 2);
                    return s;
                }
                catch { }
            }
            return "";
        }

        private void DiCosaSiTratta()
        {
            string prima = VersioneInstallata();
            if (prima.Length == 0)
            {
                Log("Prima installazione su questo computer.");
            }
            else if (prima == Versione.Numero)
            {
                Log("Qui c'e' gia' la " + prima + ": la reinstallo daccapo.");
            }
            else
            {
                Log("Trovata la versione " + prima + ": aggiorno alla " +
                    Versione.Numero + ".");
            }
            Log("");
        }

        private void ApriRegistro(bool aperto)
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { ApriRegistro(aperto); });
                return;
            }
            if (registroAperto == aperto) return;
            registroAperto = aperto;
            barra.Aperto = aperto;
            barra.Invalidate();
            Disponi();
        }

        // Finche' il registro e' chiuso la finestra resta corta: si
        // allunga solo quando c'e' davvero qualcosa da leggere.
        private void Disponi()
        {
            int y = 246;

            opzioni.Visible = (ruolo == 1);
            if (opzioni.Visible)
            {
                opzioni.Location = new Point(20, y);
                opzioni.Size = new Size(Largo - 40, 96);
                y += 96 + 10;
            }

            barra.Location = new Point(20, y);
            barra.Size = new Size(Largo - 40, 32);
            y += 32;

            cornice.Visible = registroAperto;
            if (registroAperto)
            {
                y += 8;
                cornice.Location = new Point(20, y);
                cornice.Size = new Size(Largo - 40, 150);
                txtLog.Location = new Point(12, 10);
                txtLog.Size = new Size(cornice.Width - 24, 130);
                y += 150;
            }

            y += 14;
            btnInstalla.Location = new Point(20, y);
            btnInstalla.Size = new Size(Largo - 40, 44);
            y += 44 + 14;

            btnRimuovi.Location = new Point(20, y);
            btnChiudi.Location = new Point(Largo - 20 - 110, y);
            firma.Location = new Point((Largo - firma.Width) / 2, y + 10);
            y += 36 + 16;

            ClientSize = new Size(Largo, y);
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
            if (Amministratore)
            {
                lblStato.Text = "privilegi di amministratore";
                lblStato.ForeColor = Stile.Verde;
            }
            else
            {
                lblStato.Text = "senza privilegi di amministratore";
                lblStato.ForeColor = Stile.Rosso;
                bloccato = true;
                Comandi(false);
                Aggiorna();
                ApriRegistro(true);
                Log("NON SI PUO' INSTALLARE: mancano i privilegi di");
                Log("amministratore, e servono per scrivere nel registro,");
                Log("creare le attivita' pianificate e aprire il firewall.");
                Log("");
                Log("Chiudere questa finestra e rilanciare l'installatore");
                Log("con il tasto destro, Esegui come amministratore.");
                return;
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
            if (bloccato) attivi = false;
            schServer.Enabled = attivi;
            schClient.Enabled = attivi;
            btnRimuovi.Enabled = attivi;
            btnInstalla.Enabled = attivi && ruolo != 0;
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

            DiCosaSiTratta();

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

            DiCosaSiTratta();

            // Vanno chiusi tutti e due: se restano aperti i file sono
            // bloccati e l'aggiornamento riscriverebbe niente, senza
            // che si veda un errore.
            ChiudiSeAperto("AeraTray");
            ChiudiSeAperto("AeraControl");

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
            Log("Configurazione del server: attivita' pianificate, UAC di");
            Log("rete, profilo di rete e firewall.");
            Log("");

            string ps = Path.Combine(Path.GetTempPath(), "AeraSetupServer.ps1");
            if (!Risorse.Scrivi("ServerSetup.ps1", ps, out guaio))
            { Log("[X] script del server: " + guaio); return; }

            // Le domande le ha gia' fatte questa finestra: lo script
            // gira in silenzio e scrive qui dentro, riga per riga.
            // Prima si apriva una finestra nera a parte che restava li'
            // ad aspettare risposte.
            string argomenti =
                "-NoProfile -ExecutionPolicy Bypass -File \"" + ps + "\"" +
                " -NonInterattivo" +
                " -UtenteTask \"" + utenteScelto.Replace("\"", "") + "\"" +
                (segnalatoreScelto ? " -AvviaSegnalatore" : "");

            try
            {
                var psi = new ProcessStartInfo("powershell.exe", argomenti);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs d)
                    {
                        if (d.Data != null) Log(d.Data);
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs d)
                    {
                        if (d.Data != null && d.Data.Trim().Length > 0)
                            Log("[X] " + d.Data);
                    };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                }
                Log("");
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
