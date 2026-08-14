/*
 *  AeraControl - Console di gestione remota applicativi Aera
 *
 *  Si compila con compila.cmd, nella cartella qui sopra, che usa il
 *  compilatore C# gia' presente in Windows. Nessuna dipendenza
 *  esterna, e sulle macchine di destinazione non si compila niente:
 *  arriva tutto gia' fatto dentro Setup-AeraControl.exe.
 *
 *  Compatibile C# 5 / .NET Framework 4.x
 *
 *  NOTE DI PROGETTO
 *  - Nessuna chiamata di rete sul thread dell'interfaccia: ogni comando
 *    verso il server gira su un thread separato con timeout, altrimenti
 *    un firewall chiuso fa apparire l'applicazione bloccata.
 *  - Lo stato dei processi viene letto con tasklist quando disponibile
 *    (da' PID e memoria) e con schtasks come riserva.
 *  - schtasks e tasklist NON riusano la sessione SMB aperta da net use:
 *    autenticano via RPC/DCOM con il token di chi li lancia, che in
 *    workgroup il server non conosce. Percio' ogni chiamata porta
 *    /u e /p espliciti; net use resta come verifica delle credenziali
 *    in fase di configurazione. Sul server serve la regola firewall
 *    "Gestione remota attivita' pianificate", che apre l'installatore
 *    quando si sceglie il ruolo di server.
 *  - Avvio, arresto e riavvio passano sempre da schtasks.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("AeraControl")]
[assembly: AssemblyCompany("IOTATEC srl")]
[assembly: AssemblyProduct("AeraControl")]
[assembly: AssemblyVersion(AeraControl.Versione.Numero + ".0")]
[assembly: AssemblyFileVersion(AeraControl.Versione.Numero + ".0")]

namespace AeraControl
{
    // ------------------------------------------------------------------
    // Numero unico del prodotto. Client, segnalatore del server e
    // installatore escono sempre insieme e portano lo stesso numero:
    // con due numerazioni separate, guardando una macchina non si
    // poteva dire se fosse allineata all'altra.
    //
    // Va cambiato nello stesso momento in AeraControl.cs, AeraTray.cs e
    // SetupAera.cs: la compilazione si ferma se i tre non coincidono.
    // ------------------------------------------------------------------
    public static class Versione
    {
        public const string Numero = "1.6.8";
    }

    // ------------------------------------------------------------------
    public class AppInfo
    {
        public string Titolo;
        public string NomeTask;   // chiave: nome dell'attivita' o del servizio
        public string Processo;
        public string Percorso;

        // Valorizzato solo per i servizi di Windows, che non si
        // comandano con schtasks ma con sc.
        public string Servizio;

        public bool EServizio
        {
            get { return Servizio != null && Servizio.Length > 0; }
        }

        public AppInfo(string titolo, string task, string processo, string percorso)
        {
            Titolo = titolo;
            NomeTask = task;
            Processo = processo;
            Percorso = percorso;
            Servizio = "";
        }

        public AppInfo(string titolo, string servizio, string processo,
                       string percorso, bool servizioDiWindows)
        {
            Titolo = titolo;
            NomeTask = servizio;
            Processo = processo;
            Percorso = percorso;
            Servizio = servizioDiWindows ? servizio : "";
        }
    }

    // ------------------------------------------------------------------
    public class StatoApp
    {
        public bool InEsecuzione;
        public string Pid = "";
        public string Sessione = "";
        public string Memoria = "";
        public bool DettagliDisponibili;

        // Un servizio che su quel server non c'e' proprio: va detto,
        // altrimenti sembra soltanto fermo.
        public bool NonInstallato;
    }

    // ------------------------------------------------------------------
    // Configurazione persistente, password cifrata DPAPI
    // ------------------------------------------------------------------
    public static class Config
    {
        public static string Server = "";
        public static string Utente = "";
        public static string Password = "";

        // Opzioni di avvio. Stanno dopo le prime tre righe, in forma
        // chiave=valore: i file salvati dalle versioni precedenti
        // continuano a leggersi senza conversioni.
        public static bool AvvioConWindows = false;
        public static bool AvvioApplicativi = false;
        public static List<string> DaAvviare = new List<string>();

        // Se il pulsante Palmari debba anche riavviare il proxy
        // Orderman quando lo trova gia' acceso. Spento di serie:
        // fermarlo stacca i palmari che ci stanno lavorando sopra, e
        // quasi sempre non e' quello che si vuole. Chi ha bisogno che
        // riparta insieme agli altri lo accende.
        public static bool RiavviaProxyOrderman = false;

        // Rispondendo No alla proposta di installazione la domanda non
        // va piu' riproposta a ogni avvio.
        public static bool NienteInstallazione = false;

        private static string Cartella
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AeraControl");
            }
        }

        private static string FileConfig
        {
            get { return Path.Combine(Cartella, "config.dat"); }
        }

        public static bool Esiste
        {
            get { return File.Exists(FileConfig); }
        }

        public static void Carica()
        {
            if (!Esiste) return;

            try
            {
                string[] righe = File.ReadAllLines(FileConfig, Encoding.UTF8);
                if (righe.Length < 3) return;

                Server = righe[0];
                Utente = righe[1];

                byte[] cifrata = Convert.FromBase64String(righe[2]);
                byte[] chiara = ProtectedData.Unprotect(cifrata, null,
                                                        DataProtectionScope.CurrentUser);
                Password = Encoding.UTF8.GetString(chiara);

                AvvioConWindows = false;
                AvvioApplicativi = false;
                RiavviaProxyOrderman = false;
                DaAvviare = new List<string>();

                for (int i = 3; i < righe.Length; i++)
                {
                    string r = righe[i];
                    int uguale = r.IndexOf('=');
                    if (uguale <= 0) continue;

                    string chiave = r.Substring(0, uguale).Trim();
                    string valore = r.Substring(uguale + 1).Trim();

                    if (string.Equals(chiave, "avvioConWindows", StringComparison.OrdinalIgnoreCase))
                        AvvioConWindows = (valore == "1");
                    else if (string.Equals(chiave, "avvioApplicativi", StringComparison.OrdinalIgnoreCase))
                        AvvioApplicativi = (valore == "1");
                    else if (string.Equals(chiave, "nienteInstallazione", StringComparison.OrdinalIgnoreCase))
                        NienteInstallazione = (valore == "1");
                    else if (string.Equals(chiave, "riavviaProxyOrderman", StringComparison.OrdinalIgnoreCase))
                        RiavviaProxyOrderman = (valore == "1");
                    else if (string.Equals(chiave, "daAvviare", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string t in valore.Split(','))
                            if (t.Trim().Length > 0) DaAvviare.Add(t.Trim());
                    }
                }
            }
            catch
            {
                Server = "";
                Utente = "";
                Password = "";
            }
        }

        public static void Salva()
        {
            if (!Directory.Exists(Cartella))
                Directory.CreateDirectory(Cartella);

            byte[] chiara = Encoding.UTF8.GetBytes(Password);
            byte[] cifrata = ProtectedData.Protect(chiara, null,
                                                   DataProtectionScope.CurrentUser);

            var righe = new List<string>();
            righe.Add(Server);
            righe.Add(Utente);
            righe.Add(Convert.ToBase64String(cifrata));
            righe.Add("avvioConWindows=" + (AvvioConWindows ? "1" : "0"));
            righe.Add("avvioApplicativi=" + (AvvioApplicativi ? "1" : "0"));
            righe.Add("nienteInstallazione=" + (NienteInstallazione ? "1" : "0"));
            righe.Add("riavviaProxyOrderman=" + (RiavviaProxyOrderman ? "1" : "0"));
            righe.Add("daAvviare=" + string.Join(",", DaAvviare.ToArray()));

            File.WriteAllLines(FileConfig, righe.ToArray(), Encoding.UTF8);
        }
    }

    // ------------------------------------------------------------------
    // Icone
    // ------------------------------------------------------------------
    public static class Icone
    {
        // WinForms mette una sua icona predefinita nella barra del
        // titolo, quella grigia a quadretti: non usa affatto l'icona
        // incorporata nell'eseguibile. Va presa e assegnata a mano.
        private static Icon nostra;
        private static bool cercata;

        public static Icon Applicazione
        {
            get
            {
                if (cercata) return nostra;
                cercata = true;

                // icona.ico contiene piu' misure, dalla 16 alla 256:
                // prendendola dal file Windows sceglie quella giusta
                // per ogni posto. ExtractAssociatedIcon invece rende
                // solo la 32, che rimpicciolita nella barra del titolo
                // diventa illeggibile.
                try
                {
                    string f = Path.Combine(
                        Path.GetDirectoryName(Application.ExecutablePath), "icona.ico");
                    if (File.Exists(f)) { nostra = new Icon(f); return nostra; }
                }
                catch { }

                try { nostra = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch { nostra = null; }
                return nostra;
            }
        }

        public static void Applica(Form f)
        {
            try { if (Applicazione != null) f.Icon = Applicazione; }
            catch { }
        }

        // ---- icone dei singoli applicativi -------------------------
        private static string Cartella
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AeraControl\\icone");
            }
        }

        private static string FileCache(string task)
        {
            return Path.Combine(Cartella, task + ".png");
        }

        // Icone incorporate nell'eseguibile, in PNG codificato base64.
        // Sono la copia di quelle degli applicativi: cosi' ci sono
        // sempre, anche quando il server non concede la lettura degli
        // eseguibili. Se invece la concede, quelle lette dal server
        // hanno la precedenza e queste restano il ripiego.
        private static readonly Dictionary<string, string> Incorporate =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Aera_Service",
              "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMA" +
              "AA7DAcdvqGQAAAPGSURBVFhHxZXvS5tXFMf7B/iiOrQVrKKyMaygL2S46hupMrWzCkJfWKm+mXthXFtIoy1jFZdi6Si0CINZRC0V" +
              "LEUStVWhAadu1UzTtMuSvVmj0YSmaDpLGhPyw+84dzwPuc/zmHV5QvuFA957n3PPx3PPOTmED6xD0o33rYQAW74Apq0eTD51w/N6" +
              "T3qcEikC/ObcwZe3lpDeZcBhzb/20TdGdD94jkg0Jv1clWQA955sIPP8JNK7jDIjEN39Z1IXVeIAjE/dyFAIHG907vDsxrupkgjg" +
              "3Q0i79JDWUAl00/b+VtUSAS4arCxFEuDCSbUAlnXPQt/iwqJAJ99/1gWVAj8xc2fYbJ7WVc4PG+wsf2Wv0WFRICjF5UL73O9CcFw" +
              "lPdKoUSA49/OyoKTXX/k4D1SLBHg69FVWXCyr4ZXeY8USwSwbPgUW5BmgmXdx3slUDS2j9+3/pZuHyhuDly6/0zWCYe7DPjk8gys" +
              "rtfxnyrK/GIHJ3+YR8+D59KjA8UBhCJRnL69JIMgO3JhEt8ZbFiXdMCOP4RxswsNt5eQrjHg6MUp1invKtkofhuK4OxPy4oQwhyg" +
              "jJy4ZsKnV2bE34t0jRHHtNN4/MdL6ZUJJQMg7e/vY+QXJz7umVEEkUJR7Zy7s4KNnf8/HxQBBAVCEdz9dR1nfnyCfN1Dbhrmaqdx" +
              "6tYibsz8ib9e+aWuiMViWFlZgdFohMNxcCsnBIgXZeXNXhgvd/ewGwiz9UGy2+0oLi5GUVERGhoakJOTg/r6evh88m56Z4D/0vz8" +
              "PAtWWlqKzMxMDA4OipDhcBgajQbV1dUy8JQAGAwGZGdnY2RkBBaLBVNTU9JPEIlEUFhYiNVVfrCpBqD/qKCgAAsLC+Ke1WrFxMQE" +
              "nE4n921raytGR0e5PdUAm5ubSEtLYyBUeO3t7cjNzUVjYyOampqwvb3NnoDU3NyMqqoqrhZUAYyNjSEjI4MVWDAYxPj4OMrKyhAI" +
              "BNh5NBpl797d3c3WBNLZ2YmamhqxFpIGoNaiYrPZbOK6rq4OLS0t8Hg8bG9xcRH5+fkoLy+H2+1me0ItrK2tsXXSAL29vdBqteKa" +
              "0pqXl4fKykox5ZSBkpISmEymOE8wSKEWkgbQ6XTQ6/XcntlshsvlQn9/P4aGhlBbW8vSTbURr4qKCszNzbG/kwaYnZ1lg0Z4b0F+" +
              "vx99fX2sGAcGBhAKhbhzalEqUmE/aQAqIgpCKaZAw8PDCY0y0tHRgaysLCwvL4v3JA1AIgjqd7q4ra0toREsPY3X6+XuUAWQCn1w" +
              "gH8A3oJKtnzrKP8AAAAASUVORK5CYII=" },
            { "Aera_RemoteServer",
              "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMA" +
              "AA7DAcdvqGQAAAXJSURBVFhHtZd5TFRXGMW/wbYJWpP6T2tTbRqJ1lq1Lo0aFRXaf6ytbUzdUDDWqkRRVFbZVwVZRBABZWulbILg" +
              "rgjIUgYoyMg6MGCrWFmdjVlYZ05zn2xONUwyeJJfMnnvu+c797575+URvR2ZH7M9g507HLDO1OK5yZzVucbGM/YT0QdE9I5u8aTL" +
              "zGyzv5vrJVgfDMHWnz1gtsEWs2evFhGR6XCItytT001nnJ2i8eu+QPz0owvWrjmMmR8tUxKRDRF9pls/qbp7t2pGUFBsWkT4Vdge" +
              "Dcem75ywcoU1Zs36FkTkS0QLdMe8IqMplubLl7slnQ64Xpf4WzEKCoT/o7CwEaVlLRAInqKh4TlaWjrR2ipGR4cMYrECg4MaaDRa" +
              "qFT9EIme4/LlXOyxcmMBgolosW7PUc3+9Fg4a/o29OTJCxbgLBEt0e07Km/vbEil/fD2ESDnfhvuM3LbkDtC3kvyxjFybaSG1bNx" +
              "bPy9nDacDavjPKurW/ULEBcvgkikRnBkJbyCSuAdzIdPMB++IXz4hr7Ebxwj19h9Vsfq2TjPwBIEna+EUKhGXJwIDwqE+gVgybu7" +
              "NTCx8MHmF3sMgnl0dAwhJ6cNGRkV+gVgSyiRaDDPwh9WEhuDYB5sMmxSUVH5+gXIy2+HXK7Bgl0BOCJzMgjmwSaTm9cOX79r+gUo" +
              "Lu5Ej0KDxbuC4KrwMQjmIZNpUFTcCeatV4Dy8m4olVos3R2KQFWIQTAPtppl5d36B6gSiKFSafG1ZRgie6MMgnn09GjwsEoML30C" +
              "eHllo7ZWCpVai5WWEfi9P9EgmIdCoUF1jRSenlkTB/DwzEKDUA61WovVlpHIHEwzCOahUGhRVy+Du7seAdzcstDU1MM9AlOrKNwZ" +
              "umYQzIOtQIOwBy6uegQ46ZIFkUjBBVhvFY0H2jsGwTzYHhA2KuDkfHXiAI6OV9HcrIJCqYWZ1UXwkWcQzIOdgsZGJezt9Qhwwo4F" +
              "UHOpzfZcRDkeGATzkEpZABWOHc+cOMBR20zuRcT+PCYrgFis4V5INkf0CHDocCYaG3shlkxegK6uIdTX9+KgdcbEAfYfyEB9Qy83" +
              "aP+Ju9jrkA3nsNs4ee5VXMLH0L3H6hl77bNx3D0f7e2DqK5RY98+PQLs3XsFhYW9qKxUo6amF3//04eurgFIpUOQyYe4vcGOFTvb" +
              "Y2i462yzsTqJZAgdnQN4/LgPAoEafL4a9+6pYWV1ZeIAu3Zfwa1baty8OUZ8wjNEXniC23fEKCvvxaNHfSjhSxFzqRIRF/5CSYkU" +
              "AkEfSkt7ceNmN+wci+B/ug7Z2SpkMbJesmNn+sQBzMyjkZ6u5EhLUyIhoRuFRU9RXy+FvVM+UlOVSElVIjK6FM3NchQWtWPj9+lI" +
              "TlYiNrYbBUVPMTCgQczFOoSFP0XSH0okJSlxOUmJhQu9B4kolIi+0u07KqMp27O9fYSIT1BwOJ6sQmKiCDU1Ety4VYPYOAUcnKsg" +
              "FErRJJJzzSoqumHvWAV3TyGSUx6Dz++ETNYPO4eHiInpQXRMD7bvuA6e0bIGIvIkovm6fUfF481d8P70X1oPHPwT4RFyDv+AapyL" +
              "qMCpACHCzsnh4NSAtHQRIs43oLZWgmf/ymHnUI/Qs3K4upfB3SsfyalNCAmVw8+/AxvMEsAzWtVCRDFEtJWIPtTtO17GRLSUZ2R+" +
              "d97nHopt23Lg7tEO/1My+PmP4eVbisgoAW7ebuZ++/jKOLx9pByHDtdhzdoETJ22pYvHm5lHRGFsixHRHCJ6V7fpePGIaBoRLSQi" +
              "Cx5vUSrPaG3lJ7NsOud/cWZg3foUjLB4SSSH6foUmK5LwYqV8ZhjYieZOu2HZzzeogoi4+ThZ36UiL4hollE9J5uwzeJFbKlYl8x" +
              "G9kJZa8KIjo1bMp28+sIJCIXIrImoi1EtGr4W3A622K6TfQR+5Rmgz8mIhMi+nJ4F7Oj9DoWEdHc4dnOGJ4IW9U36j8Xn7MWlA9Y" +
              "vQAAAABJRU5ErkJggg==" },
            { "Aera_RestaurantPocket",
              "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMA" +
              "AA7DAcdvqGQAAAM/SURBVFhH7ZZLTBNRFIYLiooaYTpT2k5bnkUiRqJijNEF0RhduNEFcaHxsfER586dh6WAhkl0gdG4cgMa1C1N" +
              "9J47LQWCxh0rdKMmrjQx0WhEIxrFCLTmAkq9LWCpLkz4kj/zPOfPPTdzzjgciyzyv9AYiSzx692FTOycf/5PsKxkfrDpjr/UiB5y" +
              "a7RDwtDLJGtwraopunfb1cFCPuavEbTia2TNPiUieCggGBUQJFOUEBG896hwqQiTYj42Z2qtmEc2ox0CIl8449+lkFG3Si8GUXw5" +
              "n2PBlDbHBJ9udwkIvqcZZpATwduKs7HdfJ4FUX9iqMCn0VZBga+80ewiCY9OuxqtyDI+X9asbenZIankVbrJ3HJheLzJApnPlxX1" +
              "nUMFskbZvid4g4xSSMKJYIIdSzT6cvuF+zV8zqyobo36RASP04zSRD65Ndonm/b5YDimywacCxh281YrvobPmRUuTDYKCnmXbjgj" +
              "J/v0MDlZHx4ociSTeVORybyZ8xxYeebO5uIz5D1vmrLyhN+g17fp3f+m+fh1skFQyJt042mp5FtFKNbIx/01vIYtiQgG04xnNB4w" +
              "6GmHw5F7uTPSGFniwdDGjDKYT8qt0XtrDVviQ/+YZDKvwXqwlL/9i3IMNaJKnvLGKRqVdbut7OitFXzsfNRd6V9VHiIHq8PxA7NX" +
              "0UrmyzocFxQYyWA+LfLBg+1zrGXz4ZlgcyKgwRavDrdFFYZlI8q2cXbqzP5VXo1eLlYIPwFnpMBXlwrdZTrsZFPTsqz8lBSTZa40" +
              "+0u8mr3HjekNpwIvBQUmJESelZpQm/JuZoTwQJGokKsCmmsmTHbCYZdKB7w6vVIV7jFrWnpMN6YtEiY3nSoMCQoZ+dlZJQxvKs7G" +
              "DrMq834ZqQwPFPkMu1lE8Hr+9jzVlqdaM0xwz8dFlTzyG/b+2mwHFgsoD9kNbkzvigg+sh+RdPPZRMaciDwvwbTd39wXzKlbsn2u" +
              "MGP7XNjulFR4IqowIihkbHq1ickju0bwWVThhaSSXr8OoUATXV9/orOAz7dg2P9CsCnuZ1UJ6NEjXgPwuvO97W5sqwGDHvOZdFdl" +
              "KFpda0VW57TibOC+gEUWyZof21l04K/lGrsAAAAASUVORK5CYII=" },
            { "OrdermanClassicProxy",
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
              "gFlWK9TAsqTqv8UvwrEpcW1arggAAAAASUVORK5CYII=" }
        };

        public static Image Incorporata(string task)
        {
            try
            {
                if (!Incorporate.ContainsKey(task)) return null;
                byte[] dati = Convert.FromBase64String(Incorporate[task]);
                using (var ms = new MemoryStream(dati)) return Image.FromStream(ms);
            }
            catch { return null; }
        }

        // Quella da mostrare: prima la copia presa dal server, poi
        // quella incorporata.
        public static Image Migliore(string task)
        {
            Image i = DaCache(task);
            if (i != null) return i;
            return Incorporata(task);
        }

        public static Image DaCache(string task)
        {
            try
            {
                string f = FileCache(task);
                if (!File.Exists(f)) return null;
                // Si legge in memoria: aprendo direttamente dal file
                // l'immagine terrebbe il file bloccato.
                byte[] dati = File.ReadAllBytes(f);
                using (var ms = new MemoryStream(dati)) return Image.FromStream(ms);
            }
            catch { return null; }
        }

        // Gira su thread di lavoro. Le icone stanno negli eseguibili
        // sul server: ci si arriva dalla condivisione amministrativa,
        // gia' autenticata da net use. Se non e' raggiungibile si
        // rinuncia in silenzio e restano le tessere con le iniziali.
        public static bool Scarica(AppInfo[] applicativi)
        {
            bool qualcosa = false;

            foreach (AppInfo a in applicativi)
            {
                try
                {
                    if (File.Exists(FileCache(a.NomeTask))) { qualcosa = true; continue; }

                    string p = a.Percorso;
                    if (p.Length < 3 || p[1] != ':') continue;

                    string unc = "\\\\" + Config.Server + "\\" +
                                 p.Substring(0, 1) + "$" + p.Substring(2);

                    if (!File.Exists(unc)) continue;

                    using (Icon ic = Icon.ExtractAssociatedIcon(unc))
                    {
                        if (ic == null) continue;
                        if (!Directory.Exists(Cartella)) Directory.CreateDirectory(Cartella);
                        using (Bitmap b = ic.ToBitmap())
                            b.Save(FileCache(a.NomeTask), System.Drawing.Imaging.ImageFormat.Png);
                        qualcosa = true;
                    }
                }
                catch { }
            }

            return qualcosa;
        }
    }

    // ------------------------------------------------------------------
    // Tessera: icona dell'applicativo, o iniziali se non c'e'
    // ------------------------------------------------------------------
    public class Tessera : Control
    {
        public Image Immagine;
        public string Iniziali = "";
        public Color Tinta = Color.FromArgb(90, 105, 125);

        public Tessera()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Senza questo resta un pixel scuro proprio nell'angolo
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var f = new SolidBrush(Parent != null ? Parent.BackColor : Color.White))
                g.FillRectangle(f, ClientRectangle);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            if (Immagine != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(Immagine, r);
                return;
            }

            using (GraphicsPath gp = Stile.Tondo(r, 8))
            {
                using (var f = new SolidBrush(Stile.Schiarisci(Tinta, 0.82f))) g.FillPath(f, gp);
                using (var p = new Pen(Stile.Schiarisci(Tinta, 0.55f))) g.DrawPath(p, gp);
            }

            TextRenderer.DrawText(g, Iniziali, Font, r, Stile.Scurisci(Tinta, 0.05f),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ------------------------------------------------------------------
    // Pulsante "Palmari" di AeraRestaurant
    // ------------------------------------------------------------------
    // Quel pulsante lancia i tre applicativi con percorsi scritti nel
    // codice di AeraRestaurant, che non si possono configurare, e li
    // apre sul computer dove gira AeraRestaurant invece che sul
    // server. Per mettersi in mezzo si usa la redirezione di Windows:
    // al posto dell'eseguibile parte questo stesso programma, che
    // riceve come primo argomento il percorso di quello vero.
    //
    // Si passa dal registro e non dalla sostituzione dei file perche'
    // gli aggiornamenti di Aera sovrascrivono gli eseguibili: un
    // intermediario messo al posto loro sparirebbe a ogni
    // aggiornamento, mentre il registro resta.
    // ------------------------------------------------------------------
    public static class Palmari
    {
        private const string Ramo =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
        private const string RamoWow =
            @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

        // La redirezione aggancia per NOME. Il percorso completo lo
        // controlla poi Dirotta(), perche' di AeraRemoteServer.exe ne
        // esistono due copie diverse e solo una e' il palmare.
        private static readonly string[] Nomi = new string[]
        {
            "RestaurantPocketSol.exe", "AeraRemoteServer.exe", "Aera_Service.exe"
        };

        private static string[] Rami { get { return new string[] { Ramo, RamoWow }; } }

        // La redirezione puo' stare direttamente sulla chiave del nome
        // (vecchio modo) oppure nella sottochiave filtrata per percorso
        // (modo attuale): si guardano entrambe.
        private static string Redirezione(string ramo, string nome)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine
                               .OpenSubKey(ramo + "\\" + nome, false))
                {
                    if (k == null) return "";

                    object v = k.GetValue("Debugger");
                    if (v != null && v.ToString().Trim().Length > 0)
                        return v.ToString().Trim().Trim('"');

                    using (var f = k.OpenSubKey("Aera", false))
                    {
                        if (f == null) return "";
                        object w = f.GetValue("Debugger");
                        if (w != null && w.ToString().Trim().Length > 0)
                            return w.ToString().Trim().Trim('"');
                    }
                }
            }
            catch { }
            return "";
        }

        public static bool Attivo
        {
            get
            {
                foreach (string r in Rami)
                    foreach (string n in Nomi)
                        if (Redirezione(r, n).Length > 0) return true;
                return false;
            }
        }

        // Se la redirezione punta proprio a questo eseguibile. Serve a
        // riagganciare da sola una versione precedente, che passava da
        // un programma separato.
        // Le prime versioni agganciavano la redirezione al solo nome
        // del file. Cosi' finiva di mezzo anche l'altro
        // AeraRemoteServer.exe, e soprattutto venivano intercettati i
        // segnaposto, che hanno per forza lo stesso nome. Se si trova
        // una redirezione di quel tipo va riscritta con il filtro sul
        // percorso.
        public static bool UsaFiltro
        {
            get
            {
                foreach (string r in Rami)
                {
                    foreach (string n in Nomi)
                    {
                        try
                        {
                            using (var k = Microsoft.Win32.Registry.LocalMachine
                                           .OpenSubKey(r + "\\" + n, false))
                            {
                                if (k == null) continue;
                                // Redirezione secca sul nome: vecchio stile
                                object v = k.GetValue("Debugger");
                                if (v != null && v.ToString().Trim().Length > 0) return false;
                            }
                        }
                        catch { }
                    }
                }
                return true;
            }
        }

        public static bool PuntaANoi
        {
            get
            {
                string nostro = Application.ExecutablePath;
                foreach (string r in Rami)
                {
                    foreach (string n in Nomi)
                    {
                        string s = Redirezione(r, n);
                        if (s.Length == 0) continue;
                        if (!string.Equals(s, nostro, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                }
                return Attivo;
            }
        }

        // Il sincronizzatore va acceso e spento insieme al
        // dirottamento: senza dirottamento non ci sono segnaposto da
        // tenere allineati.
        public const string NomeSincronizzatore = "AeraControlPalmari";

        // Se il dirottamento e' acceso ma il sincronizzatore non gira,
        // lo si rimette in piedi: capita a chi aggiorna da una versione
        // che non lo aveva.
        public static void AssicuraSincronizzatore()
        {
            try
            {
                if (!Attivo) return;

                foreach (Process p in Process.GetProcessesByName("AeraControl"))
                {
                    try
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        if (RigaDiComando(p).IndexOf("/sincronizza",
                                StringComparison.OrdinalIgnoreCase) >= 0)
                            return;
                    }
                    catch { }
                }

                ImpostaSincronizzatore(true);
            }
            catch { }
        }

        public static void ImpostaSincronizzatore(bool attivo)
        {
            const string ramo = @"Software\Microsoft\Windows\CurrentVersion\Run";
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ramo))
                {
                    if (k == null) return;
                    if (attivo)
                        k.SetValue(NomeSincronizzatore,
                                   "\"" + Application.ExecutablePath + "\" /sincronizza");
                    else if (k.GetValue(NomeSincronizzatore) != null)
                        k.DeleteValue(NomeSincronizzatore, false);
                }
            }
            catch { }

            // Chi sta gia' girando va fermato o fatto ripartire subito,
            // senza aspettare il prossimo accesso.
            try
            {
                foreach (Process p in Process.GetProcessesByName("AeraControl"))
                {
                    try
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        if (RigaDiComando(p).IndexOf("/sincronizza",
                                StringComparison.OrdinalIgnoreCase) >= 0)
                            p.Kill();
                    }
                    catch { }
                }
            }
            catch { }

            if (!attivo) return;

            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "/sincronizza");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
        }

        private static string RigaDiComando(Process p)
        {
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + p.Id))
                foreach (System.Management.ManagementObject o in s.Get())
                {
                    object v = o["CommandLine"];
                    if (v != null) return v.ToString();
                }
            }
            catch { }
            return "";
        }

        private static void Scrivi(Microsoft.Win32.RegistryKey padre,
                                   string sotto, string percorso)
        {
            try
            {
                using (var f = padre.CreateSubKey(sotto))
                {
                    if (f == null) return;
                    f.SetValue("FilterFullPath", percorso,
                               Microsoft.Win32.RegistryValueKind.String);
                    f.SetValue("Debugger", "\"" + Application.ExecutablePath + "\"",
                               Microsoft.Win32.RegistryValueKind.String);
                }
            }
            catch { }
        }

        // Il percorso com'e' scritto davvero su disco, maiuscole
        // comprese: si risale un pezzo alla volta chiedendolo al
        // sistema.
        private static string GrafiaReale(string percorso)
        {
            try
            {
                if (!File.Exists(percorso)) return percorso;

                string radice = Path.GetPathRoot(percorso);
                if (string.IsNullOrEmpty(radice)) return percorso;

                string[] pezzi = percorso.Substring(radice.Length)
                                         .Split(new char[] { '\\' },
                                                StringSplitOptions.RemoveEmptyEntries);
                string corrente = radice;

                foreach (string p in pezzi)
                {
                    string[] trovati = Directory.GetFileSystemEntries(corrente, p);
                    if (trovati.Length == 0) return percorso;
                    corrente = trovati[0];
                }
                return corrente;
            }
            catch { return percorso; }
        }

        // Richiede privilegi di amministratore: la chiave sta in HKLM.
        //
        // Si usa il filtro per percorso completo invece della semplice
        // redirezione per nome: cosi' viene intercettato solo
        // l'eseguibile giusto. Senza filtro finirebbero di mezzo anche
        // l'altro AeraRemoteServer.exe di C:\Aera, che e' un programma
        // diverso, e le copie usate come segnaposto.
        public static bool Imposta(bool attivo, out string errore)
        {
            errore = "";
            int fatti = 0;

            foreach (string r in Rami)
            {
                // Il ramo a 32 bit non esiste su tutti i sistemi
                bool esiste;
                try
                {
                    using (var t = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(r, false))
                        esiste = (t != null);
                }
                catch { esiste = false; }
                if (!esiste) continue;

                for (int i = 0; i < Mappa.GetLength(0); i++)
                {
                    string percorso = Mappa[i, 0];
                    string n = Path.GetFileName(percorso);

                    try
                    {
                        if (attivo)
                        {
                            using (var k = Microsoft.Win32.Registry.LocalMachine
                                           .CreateSubKey(r + "\\" + n))
                            {
                                if (k == null) continue;

                                // Una vecchia installazione poteva aver
                                // messo la redirezione senza filtro:
                                // va tolta, altrimenti vale per tutti.
                                if (k.GetValue("Debugger") != null)
                                    k.DeleteValue("Debugger", false);

                                k.SetValue("UseFilter", 1,
                                           Microsoft.Win32.RegistryValueKind.DWord);

                                // Il filtro confronta il percorso cosi'
                                // com'e' scritto: si registra anche la
                                // grafia vera letta dal disco, perche'
                                // le cartelle non sempre corrispondono
                                // nelle maiuscole e il palmare restava
                                // fermo.
                                Scrivi(k, "Aera", percorso);
                                string reale = GrafiaReale(percorso);
                                if (!string.Equals(reale, percorso, StringComparison.Ordinal))
                                    Scrivi(k, "AeraReale", reale);

                                fatti++;
                            }
                        }
                        else
                        {
                            using (var k = Microsoft.Win32.Registry.LocalMachine
                                           .OpenSubKey(r + "\\" + n, true))
                            {
                                if (k == null) continue;
                                if (k.GetValue("Debugger") != null) k.DeleteValue("Debugger", false);
                                if (k.GetValue("UseFilter") != null) k.DeleteValue("UseFilter", false);
                                try { k.DeleteSubKeyTree("Aera", false); } catch { }
                                try { k.DeleteSubKeyTree("AeraReale", false); } catch { }
                                fatti++;
                            }
                        }
                    }
                    catch (Exception ex) { errore = ex.Message; }
                }
            }

            // Il sincronizzatore parte con l'utente, non da qui: la
            // chiave Run e' quella dell'utente e questo codice gira
            // elevato, magari con un altro account.
            ImpostaSincronizzatore(attivo);

            // Ripulisce il programma separato usato dalle versioni
            // precedenti: ora l'intermediario e' questo stesso file.
            try
            {
                string vecchio = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath), "AeraShim.exe");
                if (File.Exists(vecchio)) File.Delete(vecchio);
            }
            catch { }

            if (fatti == 0 && errore.Length == 0) errore = "nessuna voce modificata";
            return fatti > 0;
        }

        // ---- funzionamento da intermediario -------------------------
        private static readonly string[,] Mappa = new string[,]
        {
            { @"C:\Aera\RestaurantPocketSol\RestaurantPocketSol.exe", "Aera_RestaurantPocket" },
            { @"C:\Aera\Remote_Function\AeraRemoteServer.exe",        "Aera_RemoteServer"     },
            { @"C:\Aera\Aera_Service.exe",                            "Aera_Service"          }
        };

        // I tre intermediari partono quasi insieme e scrivono sullo
        // stesso file: senza qualche tentativo in piu' chi trova il
        // file occupato perde la propria riga, ed e' successo.
        // Si riconosce dal NOME del file, non dal percorso completo.
        // AeraRestaurant lancia RestaurantPocketSOL.exe scrivendolo con
        // grafia diversa da quella della cartella, e su una macchina
        // nuova il confronto sul percorso intero non corrispondeva:
        // il palmare non veniva dirottato e restava fermo.
        //
        // Di AeraRemoteServer.exe esistono pero' due copie diverse:
        // solo quella sotto Remote_Function e' il palmare, l'altra in
        // C:\Aera e' un altro programma e non va toccata.
        private static string TaskDi(string percorso)
        {
            string nome, cartella;
            try
            {
                nome = Path.GetFileName(percorso);
                cartella = Path.GetDirectoryName(percorso);
            }
            catch { return null; }

            if (nome == null) return null;
            if (cartella == null) cartella = "";

            for (int i = 0; i < Mappa.GetLength(0); i++)
            {
                string suo = Path.GetFileName(Mappa[i, 0]);
                if (!string.Equals(suo, nome, StringComparison.OrdinalIgnoreCase)) continue;

                if (string.Equals(nome, "AeraRemoteServer.exe",
                                  StringComparison.OrdinalIgnoreCase) &&
                    cartella.IndexOf("Remote_Function",
                                     StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                return Mappa[i, 1];
            }
            return null;
        }

        private static void Annota(string testo)
        {
            string riga = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " +
                          testo + Environment.NewLine;
            try
            {
                string c = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AeraControl");
                if (!Directory.Exists(c)) Directory.CreateDirectory(c);
                string f = Path.Combine(c, "palmari.log");

                for (int tentativo = 0; tentativo < 12; tentativo++)
                {
                    try { File.AppendAllText(f, riga, Encoding.UTF8); return; }
                    catch (IOException) { Thread.Sleep(120); }
                    catch (UnauthorizedAccessException) { Thread.Sleep(120); }
                }
            }
            catch { }
        }

        // Riconosce di essere stato chiamato al posto di un altro
        // programma: Windows passa il percorso di quello vero.
        public static bool ChiamatoAlPostoDi(string[] argomenti)
        {
            if (argomenti.Length == 0) return false;
            string a = argomenti[0];
            if (a.StartsWith("/") || a.StartsWith("-")) return false;
            try { return a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(a); }
            catch { return false; }
        }

        public static void Dirotta(string[] argomenti)
        {
            string vero = argomenti[0];
            var resto = new List<string>();
            for (int i = 1; i < argomenti.Length; i++) resto.Add(argomenti[i]);
            string coda = string.Join(" ", resto.ToArray());

            // Chiamato al posto di un segnaposto nostro: invece di
            // rilanciarlo, cosa che riaccenderebbe la redirezione
            // all'infinito, si fa da segnaposto direttamente.
            if (vero.IndexOf("\\presenza\\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                while (true) Thread.Sleep(60000);
            }

            string task = TaskDi(vero);

            if (task == null)
            {
                Annota("non gestito, avvio locale: " + vero);
                InLocale(vero, coda);
                return;
            }

            Config.Carica();

            if (Config.Server.Length == 0)
            {
                Annota("nessun server configurato, avvio locale: " + vero);
                InLocale(vero, coda);
                return;
            }

            if (EQuestaMacchina(Config.Server))
            {
                // Sul server gli applicativi devono partire davvero:
                // anche le attivita' pianificate puntano qui.
                Annota("il server e' questa macchina, avvio locale: " + vero);
                InLocale(vero, coda);
                return;
            }

            Annota("dirotto su " + Config.Server + ": " + task);

            // AeraRestaurant non chiede sempre tutti i palmari. Dove in
            // configurazione c'e' [SOL] OMB4=1, cioe' praticamente
            // ovunque, non lancia mai RestaurantPocketSol: il pulsante
            // ne accendeva due su tre, e il terzo restava fermo senza
            // che noi ne sapessimo niente, perche' non ci veniva mai
            // passato.
            //
            // Percio' non ci si limita piu' a quello che ci viene
            // consegnato: il primo intermediario che arriva riavvia
            // l'intero gruppo, gli altri si limitano a fare da
            // segnaposto.
            if (!PrimoDelGruppo())
            {
                Annota("  gruppo gia' in corso: faccio solo da segnaposto");
                Segnaposto(vero);
                return;
            }

            RiavviaGruppo(vero, coda);
        }

        // Riavvia tutti i palmari scelti in configurazione, non solo
        // quello per cui siamo stati chiamati.
        private static void RiavviaGruppo(string vero, string coda)
        {
            string intercettato = TaskDi(vero);
            var elenco = new List<AppInfo>();

            for (int i = 0; i < Mappa.GetLength(0); i++)
            {
                string nomeTask = Mappa[i, 1];

                // Elenco vuoto vuol dire "tutti": e' come si comporta
                // il resto del programma alla prima configurazione.
                if (Config.DaAvviare.Count > 0 && !Config.DaAvviare.Contains(nomeTask))
                {
                    Annota("  " + nomeTask + ": non selezionato, lo salto");
                    continue;
                }

                // Di quello intercettato si usa il percorso che ci ha
                // passato Windows: ha la grafia vera di questa macchina.
                string percorso = string.Equals(nomeTask, intercettato,
                                      StringComparison.OrdinalIgnoreCase)
                                  ? vero : Mappa[i, 0];

                elenco.Add(new AppInfo(nomeTask, nomeTask, "", percorso));
            }

            if (elenco.Count == 0)
            {
                Annota("  nessun applicativo selezionato -> avvio locale");
                InLocale(vero, coda);
                return;
            }

            // Prima si fermano tutti, poi si riavviano. Le attivita'
            // sono configurate con MultipleInstances IgnoreNew, quindi
            // avviarne una gia' in corso non farebbe assolutamente
            // niente: chi e' gia' fermo dara' errore all'arresto, senza
            // conseguenze.
            //
            // Tutti insieme, non in fila: ogni comando verso il server
            // costa una decina di secondi, e prima di questa modifica i
            // tre intermediari lavoravano comunque in parallelo. Messi
            // in fila, il pulsante Palmari ci metteva il triplo.
            AppInfo[] app = elenco.ToArray();
            var esiti = new Remoto.Esito[app.Length];

            // I tempi finiscono nel registro: senza numeri veri, su
            // "ci mette troppo" si puo' solo tirare a indovinare, e la
            // parte lenta puo' stare tanto nella rete quanto qui.
            var totale = Stopwatch.StartNew();
            var giro = Stopwatch.StartNew();

            // Fermare quello che e' gia' fermo costava sette secondi
            // buoni piu' l'attesa che segue, ed e' esattamente il caso
            // di ogni riavvio del server. Se il sincronizzatore ha
            // guardato da poco e non ha visto niente acceso, si salta.
            if (SicuramenteFermi(app))
            {
                Annota("  nessuno era acceso: salto l'arresto");
            }
            else
            {
                InParallelo(app.Length, delegate(int k)
                {
                    var t = Stopwatch.StartNew();
                    Remoto.Ferma(app[k]);
                    Annota("  arresto " + app[k].NomeTask + ": " + t.ElapsedMilliseconds + " ms");
                });
                Annota("  arresto di tutti: " + giro.ElapsedMilliseconds + " ms");

                // Ferma() torna quando schtasks ha finito, quindi
                // l'arresto e' gia' avvenuto: questa attesa serve solo a
                // dare il tempo al processo di sparire davvero. Erano
                // 2500 ms, ed era prudenza a occhio.
                Thread.Sleep(900);
            }

            giro = Stopwatch.StartNew();
            InParallelo(app.Length, delegate(int k)
            {
                var t = Stopwatch.StartNew();
                esiti[k] = Remoto.Avvia(app[k]);
                Annota("  avvio " + app[k].NomeTask + ": " + t.ElapsedMilliseconds + " ms");
            });
            Annota("  avvio di tutti: " + giro.ElapsedMilliseconds + " ms");
            Annota("  dal comando alle finestre: " + totale.ElapsedMilliseconds + " ms");

            bool almenoUno = false;
            for (int i = 0; i < app.Length; i++)
            {
                if (esiti[i] != null && esiti[i].Ok)
                {
                    Annota("  " + app[i].NomeTask + ": avviato");
                    Segnaposto(app[i].Percorso);
                    almenoUno = true;
                }
                else Annota("  " + app[i].NomeTask + ": FALLITO: " +
                            (esiti[i] == null ? "errore imprevisto" : esiti[i].Messaggio));
            }

            if (!almenoUno)
            {
                Annota("  nessuno avviato -> avvio locale");
                Avviso("Server non raggiungibile: i palmari partono qui");
                InLocale(vero, coda);
                return;
            }

            OrdermanPerUltimo();
            Avviso("Palmari avviati sul server " + Config.Server);
        }

        // Esegue la stessa operazione su tutti gli applicativi nello
        // stesso momento e aspetta che abbiano finito tutti.
        private static void InParallelo(int quanti, Action<int> cosa)
        {
            var fili = new Thread[quanti];
            for (int i = 0; i < quanti; i++)
            {
                int k = i;   // senza copia i fili leggerebbero l'ultimo valore
                fili[i] = new Thread(delegate()
                {
                    try { cosa(k); }
                    catch { }
                });
                fili[i].IsBackground = true;
                fili[i].Start();
            }
            for (int i = 0; i < quanti; i++) fili[i].Join();
        }

        // Gli intermediari partono nello stesso istante: senza
        // un'apertura esclusiva del segno si crederebbero primi tutti,
        // e il gruppo verrebbe riavviato piu' volte di seguito.
        private static bool PrimoDelGruppo()
        {
            string segna;
            try
            {
                string c = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AeraControl");
                if (!Directory.Exists(c)) Directory.CreateDirectory(c);
                segna = Path.Combine(c, "ultimo_gruppo.txt");
            }
            catch { return true; }

            for (int tentativo = 0; tentativo < 40; tentativo++)
            {
                try
                {
                    using (var f = new FileStream(segna, FileMode.OpenOrCreate,
                                                 FileAccess.ReadWrite, FileShare.None))
                    {
                        string testo = new StreamReader(f).ReadToEnd().Trim();
                        long q;
                        if (long.TryParse(testo, out q))
                        {
                            var t = new DateTime(q, DateTimeKind.Utc);
                            if ((DateTime.UtcNow - t).TotalSeconds < 30) return false;
                        }

                        f.SetLength(0);
                        var s = new StreamWriter(f);
                        s.Write(DateTime.UtcNow.Ticks.ToString());
                        s.Flush();
                        return true;
                    }
                }
                catch (IOException) { Thread.Sleep(150); }
                catch (UnauthorizedAccessException) { Thread.Sleep(150); }
            }
            return true;
        }

        private static bool EQuestaMacchina(string server)
        {
            try
            {
                if (string.Equals(server, Environment.MachineName,
                                  StringComparison.OrdinalIgnoreCase)) return true;
                if (server == "127.0.0.1" || server == "localhost" || server == "::1")
                    return true;

                IPAddress[] miei = Dns.GetHostAddresses(Dns.GetHostName());
                IPAddress[] suoi = Dns.GetHostAddresses(server);
                foreach (IPAddress a in suoi)
                    foreach (IPAddress b in miei)
                        if (a.Equals(b)) return true;
            }
            catch { }
            return false;
        }

        // Rilanciare l'eseguibile vero col suo nome farebbe scattare di
        // nuovo la redirezione, all'infinito: si passa da una copia con
        // un altro nome, tenuta nella stessa cartella perche' li'
        // l'applicativo trova le sue librerie, e rifatta quando
        // l'originale cambia.
        private static void InLocale(string vero, string argomenti)
        {
            try
            {
                if (!File.Exists(vero)) { Annota("  non esiste: " + vero); return; }

                string cartella = Path.GetDirectoryName(vero);
                string copia = Path.Combine(cartella,
                    Path.GetFileNameWithoutExtension(vero) + "__locale.exe");

                var o = new FileInfo(vero);
                bool rifare = true;
                if (File.Exists(copia))
                {
                    var c = new FileInfo(copia);
                    rifare = (c.Length != o.Length) || (c.LastWriteTimeUtc != o.LastWriteTimeUtc);
                }
                if (rifare)
                {
                    File.Copy(vero, copia, true);
                    File.SetLastWriteTimeUtc(copia, o.LastWriteTimeUtc);
                }

                var psi = new ProcessStartInfo(copia, argomenti);
                psi.UseShellExecute = false;
                psi.WorkingDirectory = cartella;
                Process.Start(psi);
            }
            catch (Exception ex) { Annota("  avvio locale non riuscito: " + ex.Message); }
        }

        // AeraRestaurant accende la sua spia guardando i processi in
        // esecuzione su questo computer. Dirottando l'avvio sul server,
        // qui non resta niente e la spia resta spenta anche se i
        // palmari stanno lavorando.
        //
        // Si lascia quindi acceso un processo segnaposto con il nome
        // dell'applicativo: e' una copia di questo stesso programma,
        // avviata con /presenza, che non fa altro che esistere. Non e'
        // intercettata perche' la redirezione e' agganciata al percorso
        // esatto sotto C:\Aera, non al nome.
        // Il proxy Orderman non e' fra gli eseguibili che AeraRestaurant
        // lancia col pulsante Palmari, quindi da solo non partirebbe
        // mai. Va avviato per ultimo, quando gli altri sono gia' su.
        //
        // Si avvia soltanto, non si riavvia mai: fermarlo stacca i
        // palmari che ci stanno lavorando sopra. Se e' gia' acceso vuol
        // dire che qualcuno lo sta usando, e chi sta premendo Palmari
        // vuole rimettere in piedi gli applicativi, non buttare giu' i
        // dispositivi collegati.
        //
        // I tre intermediari partono quasi insieme: il primo che arriva
        // segna il passaggio e se ne occupa, gli altri lasciano stare.
        private static void OrdermanPerUltimo()
        {
            const string NomeServizio = "OrdermanClassicProxy";

            try
            {
                string segna = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AeraControl\\ultimo_orderman.txt");

                if (File.Exists(segna))
                {
                    long q;
                    if (long.TryParse(File.ReadAllText(segna).Trim(), out q))
                    {
                        var t = new DateTime(q, DateTimeKind.Utc);
                        if ((DateTime.UtcNow - t).TotalSeconds < 40) return;
                    }
                }
                // Si segna subito, prima di aspettare: altrimenti gli
                // altri due farebbero in tempo a entrare anche loro.
                File.WriteAllText(segna, DateTime.UtcNow.Ticks.ToString());
            }
            catch { }

            // Per ultimo davvero: si lascia il tempo agli altri tre di
            // avviarsi sul server.
            Thread.Sleep(8000);

            // sc non prende credenziali sulla riga di comando: si
            // appoggia alla sessione di rete, che va quindi aperta.
            Remoto.Connetti();

            // Non tutte le macchine hanno il proxy: se non e'
            // installato non c'e' niente da fare, e non e' un errore.
            bool leggibile, assente;
            string perche;
            bool attivo = Remoto.ServizioAttivo(NomeServizio, out leggibile,
                                                out perche, out assente);

            if (assente)
            {
                Annota("  proxy Orderman non installato sul server: niente da fare");
                return;
            }
            if (!leggibile)
            {
                Annota("  stato del proxy Orderman non leggibile: " + perche);
                return;
            }

            var proxy = new AppInfo("Orderman Classic Proxy", NomeServizio,
                                    "ClassicProxyService.exe",
                                    @"C:\Program Files\Orderman\ClassicProxy\ClassicProxyService.exe",
                                    true);

            if (attivo && !Config.RiavviaProxyOrderman)
            {
                // Fermarlo stacca i palmari collegati, percio' di norma
                // si lascia dov'e'. Chi ha bisogno che riparta insieme
                // agli altri accende la spunta nella configurazione.
                Annota("  proxy Orderman gia' attivo: lo lascio dov'e'");
                return;
            }

            if (attivo)
            {
                Annota("  proxy Orderman gia' attivo: lo riavvio, come da impostazioni");
                Remoto.Esito f = Remoto.Ferma(proxy);
                Annota(f.Ok ? "    fermato" : ("    arresto FALLITO: " + f.Messaggio));
                Thread.Sleep(4000);
            }
            else Annota("  avvio del proxy Orderman, per ultimo");

            Remoto.Esito e = Remoto.Avvia(proxy);
            Annota(e.Ok ? "    riuscito" : ("    FALLITO: " + e.Messaggio));
        }

        // Tiene i segnaposto allineati a quello che gira sul server.
        //
        // Senza questo la spia di AeraRestaurant restava grigia quando
        // gli applicativi erano gia' attivi sul server e qui non si era
        // premuto niente: i segnaposto nascevano solo premendo Palmari.
        public static void Sincronizza()
        {
            Config.Carica();
            if (Config.Server.Length == 0) return;
            if (EQuestaMacchina(Config.Server)) return;

            var elenco = new List<AppInfo>();
            for (int i = 0; i < Mappa.GetLength(0); i++)
                elenco.Add(new AppInfo(Mappa[i, 1], Mappa[i, 1], "", Mappa[i, 0]));

            AppInfo[] app = elenco.ToArray();
            string errore; bool dettagli;
            Dictionary<string, StatoApp> stato =
                Remoto.LeggiStato(app, out errore, out dettagli);

            var accesi = new List<string>();

            for (int i = 0; i < app.Length; i++)
            {
                bool attivo = stato.ContainsKey(app[i].NomeTask) &&
                              stato[app[i].NomeTask].InEsecuzione;

                if (attivo) { Segnaposto(app[i].Percorso); accesi.Add(app[i].NomeTask); }
                else SpegniSegnaposto(app[i].NomeTask);
            }

            // Solo se il server ha davvero risposto: una lettura fallita
            // sembrerebbe "niente in esecuzione", e su quella bugia il
            // pulsante Palmari salterebbe l'arresto senza riavviare
            // niente.
            if (errore.Length == 0) ScriviSincronia(accesi);
        }

        // Quello che il sincronizzatore ha appena visto sul server.
        // Serve al pulsante Palmari per sapere, senza spendere un altro
        // giro di rete, se c'e' qualcosa da fermare.
        private static string FileSincronia
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AeraControl\\sincronia.txt");
            }
        }

        private static void ScriviSincronia(List<string> accesi)
        {
            try
            {
                string c = Path.GetDirectoryName(FileSincronia);
                if (!Directory.Exists(c)) Directory.CreateDirectory(c);

                var righe = new List<string>();
                righe.Add("# cosa girava sul server all'ultima lettura");
                righe.Add(DateTime.UtcNow.Ticks.ToString());
                foreach (string t in accesi) righe.Add(t);

                File.WriteAllLines(FileSincronia, righe.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        // true = si e' ragionevolmente sicuri che sul server non giri
        // nessuno di questi, e l'arresto si puo' saltare. Nel dubbio
        // torna false: fermare per niente costa qualche secondo,
        // saltare l'arresto sbagliando vuol dire non riavviare, perche'
        // le attivita' sono IgnoreNew e un avvio su una gia' in corso
        // non fa niente.
        private static bool SicuramenteFermi(AppInfo[] app)
        {
            try
            {
                if (!File.Exists(FileSincronia)) return false;

                string[] righe = File.ReadAllLines(FileSincronia, Encoding.UTF8);
                long q = 0;
                var visti = new List<string>();

                foreach (string r in righe)
                {
                    string s = r.Trim();
                    if (s.Length == 0 || s.StartsWith("#")) continue;
                    if (q == 0 && long.TryParse(s, out q)) continue;
                    visti.Add(s);
                }

                if (q == 0) return false;

                // Il sincronizzatore passa ogni 30 secondi: oltre il
                // doppio, la fotografia e' troppo vecchia per fidarsi.
                var quando = new DateTime(q, DateTimeKind.Utc);
                if ((DateTime.UtcNow - quando).TotalSeconds > 70) return false;

                for (int i = 0; i < app.Length; i++)
                    if (visti.Contains(app[i].NomeTask)) return false;

                return true;
            }
            catch { return false; }
        }

        // Resta acceso in sottofondo e ricontrolla ogni mezzo minuto,
        // cosi' la spia e' giusta anche se gli applicativi vengono
        // avviati o fermati da un'altra parte.
        public static void SincronizzaSempre()
        {
            while (true)
            {
                try { Sincronizza(); }
                catch { }
                Thread.Sleep(30000);
            }
        }

        // Posto fisso e prevedibile, accanto all'applicazione
        // installata: non dipende da dove si trova l'eseguibile che ha
        // fatto da intermediario.
        public static string CartellaSegnaposto
        {
            get { return Path.Combine(Installazione.Cartella, "presenza"); }
        }

        private static void Segnaposto(string vero)
        {
            try
            {
                string nome = Path.GetFileNameWithoutExtension(vero);

                // Se c'e' gia' qualcosa con quel nome non serve altro
                try { if (Process.GetProcessesByName(nome).Length > 0) return; }
                catch { }

                string cartella = CartellaSegnaposto;
                if (!Directory.Exists(cartella)) Directory.CreateDirectory(cartella);

                string copia = Path.Combine(cartella, nome + ".exe");
                var mio = new FileInfo(Application.ExecutablePath);
                bool rifare = true;
                if (File.Exists(copia))
                {
                    var c = new FileInfo(copia);
                    rifare = (c.Length != mio.Length) ||
                             (c.LastWriteTimeUtc != mio.LastWriteTimeUtc);
                }
                if (rifare)
                {
                    File.Copy(Application.ExecutablePath, copia, true);
                    File.SetLastWriteTimeUtc(copia, mio.LastWriteTimeUtc);
                }

                var psi = new ProcessStartInfo(copia, "/presenza");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = cartella;
                Process.Start(psi);
                Annota("  segnaposto acceso: " + nome);
            }
            catch (Exception ex) { Annota("  segnaposto non riuscito: " + ex.Message); }
        }

        // Spegne il segnaposto di UN solo applicativo, quando lo si
        // ferma. Prima li spegneva tutti e tre: fermandone uno la spia
        // si spegneva anche per gli altri due, che restavano accesi.
        public static void SpegniSegnaposto(string nomeTask)
        {
            string nome = null;
            for (int i = 0; i < Mappa.GetLength(0); i++)
                if (string.Equals(Mappa[i, 1], nomeTask, StringComparison.OrdinalIgnoreCase))
                    nome = Path.GetFileNameWithoutExtension(Mappa[i, 0]);

            if (nome == null) return;

            try
            {
                foreach (Process p in Process.GetProcessesByName(nome))
                {
                    // Solo i nostri: quelli veri stanno sotto C:\Aera
                    try
                    {
                        string dove = p.MainModule.FileName;
                        if (dove.IndexOf("\\presenza\\", StringComparison.OrdinalIgnoreCase) >= 0)
                            p.Kill();
                    }
                    catch { }
                }
            }
            catch { }
        }

        // I tre partono uno dietro l'altro: senza un freno
        // comparirebbero tre avvisi uguali di fila.
        private static void Avviso(string testo)
        {
            try
            {
                string c = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AeraControl");
                string segna = Path.Combine(c, "ultimo_avviso.txt");
                if (File.Exists(segna))
                {
                    long q;
                    if (long.TryParse(File.ReadAllText(segna).Trim(), out q))
                    {
                        var t = new DateTime(q, DateTimeKind.Utc);
                        if ((DateTime.UtcNow - t).TotalSeconds < 12) return;
                    }
                }
                File.WriteAllText(segna, DateTime.UtcNow.Ticks.ToString());
            }
            catch { }

            try
            {
                var f = new Form();
                f.FormBorderStyle = FormBorderStyle.None;
                f.ShowInTaskbar = false;
                f.TopMost = true;
                f.StartPosition = FormStartPosition.Manual;
                f.BackColor = Stile.Testata;
                f.ClientSize = new Size(380, 66);

                Rectangle a = Screen.PrimaryScreen.WorkingArea;
                f.Location = new Point(a.Right - f.Width - 16, a.Bottom - f.Height - 16);

                var l = new Label();
                l.Text = testo;
                l.ForeColor = Color.White;
                l.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
                l.TextAlign = ContentAlignment.MiddleCenter;
                l.Dock = DockStyle.Fill;
                f.Controls.Add(l);

                var chiudi = new System.Windows.Forms.Timer();
                chiudi.Interval = 3500;
                chiudi.Tick += delegate { chiudi.Stop(); f.Close(); };
                chiudi.Start();

                Application.Run(f);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // Avvio automatico all'accesso a Windows
    // ------------------------------------------------------------------
    // Si usa la chiave Run dell'utente: non serve essere amministratori
    // e vale solo per chi la imposta, che e' quello che si vuole visto
    // che anche le credenziali sono legate all'utente.
    public static class AvvioWindows
    {
        private const string Percorso = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Nome = "AeraControl";

        public static bool Attivo
        {
            get
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Percorso, false))
                    {
                        if (k == null) return false;
                        return k.GetValue(Nome) != null;
                    }
                }
                catch { return false; }
            }
        }

        public static void Imposta(bool attivo, out string errore)
        {
            errore = "";
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Percorso))
                {
                    if (k == null) { errore = "chiave di registro non accessibile"; return; }

                    if (attivo)
                        k.SetValue(Nome, "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue(Nome) != null)
                        k.DeleteValue(Nome, false);
                }
            }
            catch (Exception ex) { errore = ex.Message; }
        }
    }

    // ------------------------------------------------------------------
    // Comandi verso il server
    // ------------------------------------------------------------------
    public static class Remoto
    {
        // tasklist verso una rete filtrata puo' impiegare minuti prima
        // di arrendersi: si taglia corto.
        public const int TimeoutBreve = 8000;
        public const int TimeoutNormale = 25000;

        // tasklist e' solo un di piu' (PID e memoria) e sui server
        // provati impiega oltre venti secondi prima di fallire: si
        // aspetta poco, tanto lo stato arriva comunque da schtasks.
        public const int TimeoutTasklist = 4000;

        // L'elenco completo dei task e' piu' voluminoso di una singola
        // interrogazione, quindi ha un margine piu' largo.
        public const int TimeoutElenco = 15000;

        public static bool TasklistDisponibile = true;

        public class Esito
        {
            public int Codice;
            public string Output = "";
            public bool Scaduto;

            public bool Ok { get { return Codice == 0 && !Scaduto; } }

            public string Messaggio
            {
                get
                {
                    if (Scaduto) return "tempo scaduto, il server non ha risposto";
                    string t = Output.Replace("\r", " ").Replace("\n", " ").Trim();
                    while (t.Contains("  ")) t = t.Replace("  ", " ");
                    if (t.Length == 0) t = "errore " + Codice;
                    return Nascondi(t);
                }
            }
        }

        // La password viene passata sulla riga di comando: se finisse in
        // un messaggio di errore comparirebbe nel registro a video.
        public static string Nascondi(string testo)
        {
            if (testo == null) return "";
            if (Config.Password.Length > 0)
                testo = testo.Replace(Config.Password, "***");
            return testo;
        }

        // schtasks e tasklist non riusano la sessione aperta da net use:
        // autenticano per conto loro e in workgroup il token locale del
        // client non vale nulla. Le credenziali vanno ripetute a ogni
        // chiamata, altrimenti il server risponde "Accesso negato".
        private static string Credenziali()
        {
            if (Config.Utente.Length == 0) return "";
            return string.Format(" /u \"{0}\" /p \"{1}\"",
                                 Config.Utente, Config.Password);
        }

        public static Esito Esegui(string exe, string argomenti, int timeoutMs)
        {
            var esito = new Esito();
            var testo = new StringBuilder();

            try
            {
                var psi = new ProcessStartInfo(exe, argomenti);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                using (Process p = new Process())
                {
                    p.StartInfo = psi;

                    // Lettura asincrona: con ReadToEnd il timeout non
                    // avrebbe effetto, si resterebbe fermi sulla lettura.
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) lock (testo) testo.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) lock (testo) testo.AppendLine(e.Data);
                    };

                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); }
                        catch { }
                        esito.Scaduto = true;
                        esito.Codice = -1;
                    }
                    else
                    {
                        esito.Codice = p.ExitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                esito.Codice = -1;
                lock (testo) testo.AppendLine(ex.Message);
            }

            lock (testo) esito.Output = testo.ToString().Trim();
            return esito;
        }

        // --------------------------------------------------------------
        // Verifica che le credenziali siano valide e che il server sia
        // raggiungibile. I comandi veri portano le proprie credenziali,
        // ma questa resta la prova piu' chiara in configurazione.
        // Si invoca net.exe direttamente: passando da cmd.exe una
        // password con &, | o ^ verrebbe interpretata dalla shell.
        public static Esito Connetti()
        {
            // Windows non ammette due sessioni con credenziali diverse
            // verso lo stesso server: si chiude sempre la precedente.
            Esegui("net.exe",
                   "use \\\\" + Config.Server + "\\IPC$ /delete /y",
                   TimeoutBreve);

            string cmd = string.Format(
                "use \\\\{0}\\IPC$ /user:\"{1}\" \"{2}\"",
                Config.Server, Config.Utente, Config.Password);

            return Esegui("net.exe", cmd, TimeoutNormale);
        }

        public static void Disconnetti()
        {
            Esegui("net.exe",
                   "use \\\\" + Config.Server + "\\IPC$ /delete /y",
                   3000);
        }

        // --------------------------------------------------------------
        // Stato tramite tasklist: da' PID, sessione e memoria, ma usa
        // RPC/DCOM, che il firewall spesso blocca.
        // --------------------------------------------------------------
        private static Dictionary<string, StatoApp> ProvaTasklist(out string motivo)
        {
            Esito e = Esegui("tasklist.exe",
                             "/s " + Config.Server + Credenziali() + " /fo csv /nh",
                             TimeoutTasklist);

            motivo = "";
            if (!e.Ok) { motivo = e.Messaggio; return null; }

            var risultato = new Dictionary<string, StatoApp>(
                                StringComparer.OrdinalIgnoreCase);

            foreach (string riga in e.Output.Split('\n'))
            {
                string r = riga.Trim();
                if (r.Length == 0) continue;

                string[] campi = r.Split(new string[] { "\",\"" }, StringSplitOptions.None);
                if (campi.Length < 5) continue;

                string nome = campi[0].Trim('"', ' ');
                if (risultato.ContainsKey(nome)) continue;

                var s = new StatoApp();
                s.InEsecuzione = true;
                s.DettagliDisponibili = true;
                s.Pid = campi[1].Trim('"', ' ');
                s.Sessione = campi[3].Trim('"', ' ');
                s.Memoria = campi[4].Trim('"', ' ');

                risultato[nome] = s;
            }

            return risultato;
        }

        // --------------------------------------------------------------
        // Stato tramite schtasks: nessun dettaglio, ma passa da SMB come
        // la connessione, quindi funziona ovunque funzioni l'avvio.
        // Lo stato e' localizzato: si confrontano le forme note.
        // --------------------------------------------------------------
        // Lo stato e' localizzato: in italiano "In esecuzione", in
        // inglese "Running". Fermo e' "Pronta" / "Ready".
        private static bool EInEsecuzione(string stato)
        {
            if (stato == null) return false;
            string s = stato.ToLowerInvariant();
            return s.Contains("running") || s.Contains("esecuzione");
        }

        // Un'interrogazione sola per tutti i task invece di una per
        // ciascuno: verso un server remoto ogni chiamata a schtasks
        // costa diversi secondi di negoziazione, che si moltiplicavano
        // per il numero di applicativi.
        private static Dictionary<string, string> LeggiTuttiITask(out string motivo)
        {
            motivo = "";

            Esito e = Esegui("schtasks.exe",
                             string.Format("/query /s {0}{1} /fo csv /nh",
                                           Config.Server, Credenziali()),
                             TimeoutElenco);

            if (!e.Ok) { motivo = e.Messaggio; return null; }

            var stati = new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase);

            foreach (string riga in e.Output.Split('\n'))
            {
                string r = riga.Trim();
                if (r.Length == 0) continue;

                string[] campi = r.Split(new string[] { "\",\"" },
                                         StringSplitOptions.None);
                if (campi.Length < 3) continue;

                // I nomi arrivano col percorso: "\Aera_Service"
                string nome  = campi[0].Trim('"', ' ').TrimStart('\\');
                string stato = campi[campi.Length - 1].Trim('"', ' ');
                if (nome.Length == 0) continue;

                // Un task con piu' istanze compare su piu' righe:
                // basta che una lo dia attivo.
                if (stati.ContainsKey(nome))
                {
                    if (EInEsecuzione(stato)) stati[nome] = stato;
                }
                else stati[nome] = stato;
            }

            return stati;
        }

        // --------------------------------------------------------------
        // Stato scritto dal segnalatore che gira sul server.
        //
        // E' la fonte piu' fedele: guarda i processi, mentre schtasks
        // riferisce se e' in corso l'ATTIVITA'. I due divergono se
        // l'applicativo viene chiuso a mano lasciando vivo un processo
        // figlio: l'attivita' resta "in esecuzione" e il client
        // continuerebbe a dirlo attivo.
        public static bool SegnalatoreVisto = false;

        private static Dictionary<string, StatoApp> DalSegnalatore()
        {
            try
            {
                string f = "\\\\" + Config.Server + "\\C$\\iotatau\\AeraControl\\stato.txt";
                if (!File.Exists(f)) return null;

                // Se il segnalatore non gira piu', il file resta li'
                // fermo: oltre una certa eta' non ci si fida.
                DateTime scritto = File.GetLastWriteTimeUtc(f);
                if ((DateTime.UtcNow - scritto).TotalMinutes > 3) return null;

                var res = new Dictionary<string, StatoApp>(StringComparer.OrdinalIgnoreCase);

                foreach (string riga in File.ReadAllLines(f, Encoding.UTF8))
                {
                    string r = riga.Trim();
                    if (r.Length == 0 || r.StartsWith("#")) continue;

                    string[] c = r.Split('|');
                    if (c.Length < 2) continue;

                    var s = new StatoApp();
                    s.InEsecuzione = (c[1] == "1");
                    if (s.InEsecuzione && c.Length >= 4)
                    {
                        s.Pid = c[2];
                        s.Memoria = c[3] + " MB";
                        s.Sessione = "";
                        s.DettagliDisponibili = (s.Pid.Length > 0);
                    }
                    res[c[0].Trim()] = s;
                }

                return (res.Count > 0) ? res : null;
            }
            catch { return null; }
        }

        public static Dictionary<string, StatoApp> LeggiStato(
                        AppInfo[] applicativi, out string errore, out bool dettagli)
        {
            errore = "";
            dettagli = false;

            var risultato = new Dictionary<string, StatoApp>(
                                StringComparer.OrdinalIgnoreCase);

            // Prima si prova il segnalatore: e' una lettura di file,
            // quindi immediata, e dice lo stato vero dei processi.
            Dictionary<string, StatoApp> dalTray = DalSegnalatore();
            if (dalTray != null)
            {
                bool primaVolta = !SegnalatoreVisto;
                SegnalatoreVisto = true;
                dettagli = true;

                foreach (AppInfo app in applicativi)
                {
                    risultato[app.NomeTask] = dalTray.ContainsKey(app.NomeTask)
                                              ? dalTray[app.NomeTask]
                                              : new StatoApp();
                }

                if (primaVolta)
                    errore = "stato letto dal segnalatore sul server: " +
                             "e' quello vero dei processi";

                return risultato;
            }

            string nota = "";

            if (TasklistDisponibile)
            {
                string perche;
                Dictionary<string, StatoApp> daTasklist = ProvaTasklist(out perche);

                if (daTasklist != null)
                {
                    dettagli = true;

                    foreach (AppInfo app in applicativi)
                    {
                        if (daTasklist.ContainsKey(app.Processo))
                            risultato[app.NomeTask] = daTasklist[app.Processo];
                        else
                            risultato[app.NomeTask] = new StatoApp();
                    }
                    return risultato;
                }

                // Un solo tentativo: dal giro successivo si va diretti su
                // schtasks, senza pagare ogni volta il timeout.
                // Non e' un guasto: si perdono PID e memoria, lo stato
                // acceso/spento arriva lo stesso. Va detto in modo che
                // non sembri un errore.
                TasklistDisponibile = false;
                nota = "PID e memoria non disponibili (tasklist: " + perche +
                       "). Lo stato si legge da schtasks, tutto regolare.";
            }

            string perche2;
            Dictionary<string, string> stati = LeggiTuttiITask(out perche2);

            if (stati == null)
            {
                foreach (AppInfo app in applicativi)
                    risultato[app.NomeTask] = new StatoApp();

                // Il motivo vero distingue un firewall chiuso, che non
                // risponde affatto, da credenziali rifiutate, che
                // rispondono subito: senza, i due casi si somigliano.
                errore = "lettura dello stato non riuscita -> " +
                         ((perche2.Length > 0) ? perche2 : "nessuna risposta");
            }
            else
            {
                var mancanti = new List<string>();

                foreach (AppInfo app in applicativi)
                {
                    var s = new StatoApp();
                    s.DettagliDisponibili = false;

                    if (app.EServizio)
                    {
                        // I servizi non compaiono fra le attivita':
                        // vanno chiesti al gestore dei servizi.
                        bool leggibile, attivo, assente;
                        string perche3;
                        attivo = ServizioAttivo(app.Servizio, out leggibile,
                                                out perche3, out assente);
                        s.InEsecuzione = attivo;
                        s.NonInstallato = assente;
                        if (!leggibile) mancanti.Add(app.Titolo + " (servizio)");
                    }
                    else if (stati.ContainsKey(app.NomeTask))
                        s.InEsecuzione = EInEsecuzione(stati[app.NomeTask]);
                    else
                        mancanti.Add(app.NomeTask);

                    risultato[app.NomeTask] = s;
                }

                // Un'attivita' assente si nota solo cosi': altrimenti
                // resterebbe grigia come una semplicemente ferma.
                if (mancanti.Count > 0)
                    errore = "attivita' non presenti sul server: " +
                             string.Join(", ", mancanti.ToArray()) +
                             " (rieseguire Setup-AeraControl.exe sul server)";
            }

            if (nota.Length > 0)
                errore = (errore.Length > 0) ? (nota + " | " + errore) : nota;

            return risultato;
        }

        // --------------------------------------------------------------
        public static Esito Avvia(AppInfo app)
        {
            if (app.EServizio) return Servizio("start", app.Servizio);

            return Esegui("schtasks.exe",
                          string.Format("/run /s {0}{1} /tn \"{2}\"",
                                        Config.Server, Credenziali(), app.NomeTask),
                          TimeoutNormale);
        }

        // schtasks /end termina l'istanza in corso: piu' affidabile di
        // taskkill, che va per forza di RPC/DCOM.
        public static Esito Ferma(AppInfo app)
        {
            if (app.EServizio) return Servizio("stop", app.Servizio);

            return Esegui("schtasks.exe",
                          string.Format("/end /s {0}{1} /tn \"{2}\"",
                                        Config.Server, Credenziali(), app.NomeTask),
                          TimeoutNormale);
        }

        // I servizi di Windows si comandano con sc, che non prende
        // credenziali sulla riga di comando: si appoggia alla sessione
        // gia' aperta da net use, quindi qui non serve passarle.
        private static Esito Servizio(string verbo, string nome)
        {
            return Esegui("sc.exe",
                          string.Format("\\\\{0} {1} \"{2}\"",
                                        Config.Server, verbo, nome),
                          TimeoutNormale);
        }

        // Lo stato del servizio: sc scrive "STATO : 4  RUNNING" e la
        // parola RUNNING non viene tradotta, quindi si puo' cercare
        // sia quella sia il codice numerico.
        public static bool ServizioAttivo(string nome, out bool leggibile,
                                          out string motivo, out bool nonInstallato)
        {
            leggibile = false;
            motivo = "";
            nonInstallato = false;

            Esito e = Esegui("sc.exe",
                             string.Format("\\\\{0} query \"{1}\"", Config.Server, nome),
                             TimeoutBreve);

            if (!e.Ok)
            {
                motivo = e.Messaggio;
                // 1060: il servizio non esiste su quella macchina. Non
                // e' un guasto, e' un applicativo che li' non c'e'.
                string t = e.Output.ToUpperInvariant();
                if (t.Contains("1060") || t.Contains("NON ESISTE") ||
                    t.Contains("DOES NOT EXIST"))
                {
                    nonInstallato = true;
                    leggibile = true;
                }
                return false;
            }

            leggibile = true;

            foreach (string riga in e.Output.Split('\n'))
            {
                string r = riga.ToUpperInvariant();
                if (r.Contains("RUNNING")) return true;
                if (r.Contains("STOPPED") || r.Contains("STOP_PENDING")) return false;
            }
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Autoinstallazione
    // ------------------------------------------------------------------
    public static class Installazione
    {
        // Tutto sotto un'unica cartella: console, segnalatore, stato e
        // segnaposto. Prima erano sparsi fra Palmari e AeraTray.
        public const string Cartella = @"C:\iotatau\AeraControl";
        public const string NomeExe = "AeraControl.exe";
        public const string NomeCollegamento = "Aera - Console applicativi";

        public static string PercorsoInstallato
        {
            get { return Path.Combine(Cartella, NomeExe); }
        }

        public static string PercorsoCorrente
        {
            get { return Application.ExecutablePath; }
        }

        public static bool GiaInPosizione
        {
            get
            {
                return string.Equals(PercorsoCorrente, PercorsoInstallato,
                                     StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool Amministratore
        {
            get
            {
                try
                {
                    var identita = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var principale = new System.Security.Principal.WindowsPrincipal(identita);
                    return principale.IsInRole(
                        System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        public static void Installa()
        {
            if (!Directory.Exists(Cartella))
                Directory.CreateDirectory(Cartella);

            foreach (Process p in Process.GetProcessesByName("AeraControl"))
            {
                try
                {
                    if (p.Id != Process.GetCurrentProcess().Id)
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
            }

            File.Copy(PercorsoCorrente, PercorsoInstallato, true);
            CreaCollegamenti();
        }

        // Nomi usati dalle versioni precedenti: vanno tolti, altrimenti
        // sul desktop si accumulano icone diverse dello stesso
        // programma.
        private static readonly string[] NomiVecchi = new string[]
        {
            "Aera - Console applicativi"
        };

        public static void CreaCollegamenti()
        {
            string pubblico = Environment.GetFolderPath(
                                  Environment.SpecialFolder.CommonDesktopDirectory);
            string utente = Environment.GetFolderPath(
                                  Environment.SpecialFolder.DesktopDirectory);
            string menu = Environment.GetFolderPath(
                                  Environment.SpecialFolder.CommonPrograms);

            // Pulizia: i nomi vecchi ovunque, e le copie sul desktop
            // pubblico e nel menu Start. Prima se ne creavano tre, con
            // due icone identiche sullo stesso desktop.
            foreach (string c in new string[] { pubblico, utente, menu })
            {
                if (string.IsNullOrEmpty(c) || !Directory.Exists(c)) continue;
                foreach (string vecchio in NomiVecchi)
                    try { File.Delete(Path.Combine(c, vecchio + ".lnk")); } catch { }
            }
            foreach (string c in new string[] { pubblico, menu })
            {
                if (string.IsNullOrEmpty(c) || !Directory.Exists(c)) continue;
                try { File.Delete(Path.Combine(c, NomeCollegamento + ".lnk")); } catch { }
            }

            // Una icona sola, sul desktop dell'utente che sta usando il
            // computer.
            if (!string.IsNullOrEmpty(utente) && Directory.Exists(utente))
            {
                try { CreaCollegamento(Path.Combine(utente, NomeCollegamento + ".lnk")); }
                catch { }
            }
        }

        // Late binding su WScript.Shell: evita di referenziare la
        // libreria COM in fase di compilazione.
        private static void CreaCollegamento(string percorsoLnk)
        {
            Type tipo = Type.GetTypeFromProgID("WScript.Shell");
            if (tipo == null) return;

            object shell = Activator.CreateInstance(tipo);

            object lnk = tipo.InvokeMember("CreateShortcut",
                            System.Reflection.BindingFlags.InvokeMethod,
                            null, shell, new object[] { percorsoLnk });

            Type tl = lnk.GetType();
            System.Reflection.BindingFlags set = System.Reflection.BindingFlags.SetProperty;

            tl.InvokeMember("TargetPath", set, null, lnk,
                            new object[] { PercorsoInstallato });
            tl.InvokeMember("WorkingDirectory", set, null, lnk,
                            new object[] { Cartella });
            tl.InvokeMember("IconLocation", set, null, lnk,
                            new object[] { PercorsoInstallato + ",0" });
            tl.InvokeMember("Description", set, null, lnk,
                            new object[] { "Avvio e controllo degli applicativi Aera" });

            tl.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod,
                            null, lnk, null);
        }

        public static bool CollegamentoPresente
        {
            get
            {
                try
                {
                    string pubblico = Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.CommonDesktopDirectory),
                        NomeCollegamento + ".lnk");

                    string utente = Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.DesktopDirectory),
                        NomeCollegamento + ".lnk");

                    return File.Exists(pubblico) || File.Exists(utente);
                }
                catch { return false; }
            }
        }
    }

    // ------------------------------------------------------------------
    // Finestra di configurazione
    // ------------------------------------------------------------------
    public class FormConfig : Form
    {
        private TextBox txtServer, txtUtente, txtPassword;
        private Label lblEsito;
        private PulsanteTondo btnProva;
        private CheckBox chkConWindows, chkApplicativi, chkPalmari, chkProxy;
        private CheckedListBox elencoApp;
        private AppInfo[] applicativi;
        private bool palmariPrima;

        private const int Largo = 470;

        public FormConfig(AppInfo[] elenco)
        {
            applicativi = elenco;

            Text = "Configurazione  -  versione " + Versione.Numero;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Largo, 580);
            Font = new Font("Segoe UI", 9F);
            BackColor = Stile.Sfondo;
            Icone.Applica(this);

            // ---- collegamento al server ----------------------------
            Controls.Add(Titoletto("Collegamento al server", 20, 16));

            var riquadro = new Riquadro();
            riquadro.Location = new Point(20, 40);
            riquadro.Size = new Size(Largo - 40, 132);
            Controls.Add(riquadro);

            riquadro.Controls.Add(Etichetta("Server", 16, 16));
            txtServer = Casella(110, 14, false);
            txtServer.Text = Config.Server;
            riquadro.Controls.Add(txtServer);

            riquadro.Controls.Add(Etichetta("Utente", 16, 50));
            txtUtente = Casella(110, 48, false);
            txtUtente.Text = (Config.Utente.Length > 0) ? Config.Utente : "Administrator";
            riquadro.Controls.Add(txtUtente);

            var nota = new Label();
            nota.Text = "il nome del server viene aggiunto da solo";
            nota.Location = new Point(112, 74);
            nota.Size = new Size(280, 16);
            nota.ForeColor = Stile.TestoTenue;
            nota.Font = new Font("Segoe UI", 8F);
            riquadro.Controls.Add(nota);

            riquadro.Controls.Add(Etichetta("Password", 16, 96));
            txtPassword = Casella(110, 94, true);
            txtPassword.Text = Config.Password;
            riquadro.Controls.Add(txtPassword);

            // ---- avvio automatico ----------------------------------
            Controls.Add(Titoletto("Avvio automatico", 20, 186));

            var riquadro2 = new Riquadro();
            riquadro2.Location = new Point(20, 210);
            riquadro2.Size = new Size(Largo - 40, 96 + Math.Max(44, elenco.Length * 17 + 6));
            Controls.Add(riquadro2);

            chkConWindows = new CheckBox();
            chkConWindows.Text = "Apri questo programma all'accesso a Windows";
            chkConWindows.Location = new Point(16, 14);
            chkConWindows.Size = new Size(390, 20);
            chkConWindows.ForeColor = Stile.Testo;
            chkConWindows.BackColor = Color.Transparent;
            chkConWindows.Checked = Config.AvvioConWindows || AvvioWindows.Attivo;
            riquadro2.Controls.Add(chkConWindows);

            chkApplicativi = new CheckBox();
            chkApplicativi.Text = "Avvia gli applicativi sul server appena connesso";
            chkApplicativi.Location = new Point(16, 40);
            chkApplicativi.Size = new Size(390, 20);
            chkApplicativi.ForeColor = Stile.Testo;
            chkApplicativi.BackColor = Color.Transparent;
            chkApplicativi.Checked = Config.AvvioApplicativi;
            chkApplicativi.CheckedChanged += delegate { AggiornaElenco(); };
            riquadro2.Controls.Add(chkApplicativi);

            var nota2 = new Label();
            nota2.Text = "quali avviare:";
            nota2.Location = new Point(34, 64);
            nota2.Size = new Size(200, 16);
            nota2.ForeColor = Stile.TestoTenue;
            nota2.Font = new Font("Segoe UI", 8F);
            riquadro2.Controls.Add(nota2);

            elencoApp = new CheckedListBox();
            elencoApp.Location = new Point(34, 82);
            elencoApp.Size = new Size(Largo - 40 - 68, 60);
            elencoApp.BorderStyle = BorderStyle.FixedSingle;
            elencoApp.CheckOnClick = true;
            elencoApp.BackColor = Color.White;
            elencoApp.ForeColor = Stile.Testo;
            elencoApp.IntegralHeight = false;
            // Tanto alto quanti sono gli applicativi, senza scorrimento
            elencoApp.Height = Math.Max(44, applicativi.Length * 17 + 6);

            // Se non e' mai stato scelto niente si parte da tutti
            // selezionati: e' l'aspettativa piu' comune.
            bool primaVolta = (Config.DaAvviare.Count == 0);
            foreach (AppInfo a in applicativi)
            {
                bool segnato = primaVolta || Config.DaAvviare.Contains(a.NomeTask);
                elencoApp.Items.Add(a.Titolo, segnato);
            }
            riquadro2.Controls.Add(elencoApp);

            AggiornaElenco();

            // ---- pulsante Palmari di AeraRestaurant ----------------
            // Sotto il riquadro precedente, che si adatta al numero
            // di applicativi.
            int yPalmari = riquadro2.Bottom + 14;
            Controls.Add(Titoletto("Pulsante Palmari di AeraRestaurant", 20, yPalmari));

            var riquadro3 = new Riquadro();
            riquadro3.Location = new Point(20, yPalmari + 24);
            riquadro3.Size = new Size(Largo - 40, 140);
            Controls.Add(riquadro3);

            chkPalmari = new CheckBox();
            chkPalmari.Text = "Apri i palmari sul server invece che su questo PC";
            chkPalmari.Location = new Point(16, 12);
            chkPalmari.Size = new Size(390, 20);
            chkPalmari.ForeColor = Stile.Testo;
            chkPalmari.BackColor = Color.Transparent;
            chkPalmari.Checked = Palmari.Attivo;
            palmariPrima = chkPalmari.Checked;
            riquadro3.Controls.Add(chkPalmari);

            var nota3 = new Label();
            nota3.Text = "Da attivare solo sui PC dove si usa AeraRestaurant,\n" +
                         "mai sul server. Richiede i privilegi di amministratore.";
            nota3.Location = new Point(34, 34);
            nota3.Size = new Size(Largo - 80, 34);
            nota3.ForeColor = Stile.TestoTenue;
            nota3.Font = new Font("Segoe UI", 8F);
            riquadro3.Controls.Add(nota3);

            var riga = new Panel();
            riga.Location = new Point(16, 74);
            riga.Size = new Size(Largo - 72, 1);
            riga.BackColor = Stile.Bordo;
            riquadro3.Controls.Add(riga);

            chkProxy = new CheckBox();
            chkProxy.Text = "Riavvia anche il proxy Orderman, se lo trova gia' acceso";
            chkProxy.Location = new Point(16, 84);
            chkProxy.Size = new Size(420, 20);
            chkProxy.ForeColor = Stile.Testo;
            chkProxy.BackColor = Color.Transparent;
            chkProxy.Checked = Config.RiavviaProxyOrderman;
            riquadro3.Controls.Add(chkProxy);

            var nota4 = new Label();
            nota4.Text = "Fermarlo stacca i palmari collegati, percio' di norma resta\n" +
                         "acceso dov'e'. Se e' spento viene avviato in ogni caso.";
            nota4.Location = new Point(34, 106);
            nota4.Size = new Size(Largo - 80, 30);
            nota4.ForeColor = Stile.TestoTenue;
            nota4.Font = new Font("Segoe UI", 8F);
            riquadro3.Controls.Add(nota4);

            // ---- esito e pulsanti ----------------------------------
            int yEsito = riquadro3.Bottom + 10;
            int yPulsanti = yEsito + 42;
            ClientSize = new Size(Largo, yPulsanti + 34 + 16);

            lblEsito = new Label();
            lblEsito.Location = new Point(20, yEsito);
            lblEsito.Size = new Size(Largo - 40, 36);
            lblEsito.ForeColor = Stile.TestoTenue;
            Controls.Add(lblEsito);

            btnProva = new PulsanteTondo(Stile.Verde);
            btnProva.Text = "Prova e salva";
            btnProva.Size = new Size(130, 34);
            btnProva.Location = new Point(Largo - 20 - 130 - 8 - 100, yPulsanti);
            btnProva.Click += ProvaESalva;
            Controls.Add(btnProva);

            PulsanteTondo btnAnnulla = new PulsanteTondo(Color.White);
            btnAnnulla.Contorno = true;
            btnAnnulla.Text = "Annulla";
            btnAnnulla.Size = new Size(100, 34);
            btnAnnulla.Location = new Point(Largo - 20 - 100, yPulsanti);
            btnAnnulla.DialogResult = DialogResult.Cancel;
            Controls.Add(btnAnnulla);

            CancelButton = btnAnnulla;
            AcceptButton = btnProva;
        }

        private void AggiornaElenco()
        {
            if (elencoApp == null) return;
            bool attivo = chkApplicativi.Checked;
            elencoApp.Enabled = attivo;
            // Il solo Enabled non si nota: la CheckedListBox continua a
            // disegnare i segni di spunta come se fosse attiva.
            elencoApp.ForeColor = attivo ? Stile.Testo : Color.FromArgb(160, 168, 178);
            elencoApp.BackColor = attivo ? Color.White : Color.FromArgb(247, 248, 250);
        }

        // Niente maiuscolo: si legge peggio e grida senza motivo. Le
        // intestazioni si distinguono gia' per il grassetto e per il
        // colore piu' tenue, come nell'installatore.
        private Label Titoletto(string testo, int x, int y)
        {
            var l = new Label();
            l.Text = testo;
            l.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            l.ForeColor = Stile.TestoTenue;
            l.Location = new Point(x + 2, y);
            l.Size = new Size(320, 18);
            return l;
        }

        private Label Etichetta(string testo, int x, int y)
        {
            var l = new Label();
            l.Text = testo;
            l.Location = new Point(x, y + 3);
            l.Size = new Size(90, 20);
            l.ForeColor = Stile.Testo;
            return l;
        }

        private TextBox Casella(int x, int y, bool password)
        {
            var t = new TextBox();
            t.Location = new Point(x, y);
            t.Size = new Size(Largo - 40 - x - 16, 22);
            if (password) t.UseSystemPasswordChar = true;
            return t;
        }

        // Avvio con Windows e dirottamento dei palmari: riguardano
        // questo computer e si applicano comunque, anche se il server
        // non risponde. Restituisce il guaio, o vuoto se e' andata.
        private string ApplicaLocali()
        {
            // Serve anche quando la spunta non cambia ma la redirezione
            // punta ancora al vecchio programma separato: cosi' si
            // riaggancia da sola.
            bool serveElevare = (chkPalmari.Checked != palmariPrima) ||
                                (chkPalmari.Checked && !Palmari.PuntaANoi) ||
                                (chkPalmari.Checked && !Palmari.UsaFiltro);

            if (serveElevare)
            {
                try
                {
                    var psi = new ProcessStartInfo(
                        Application.ExecutablePath,
                        chkPalmari.Checked ? "/palmari:on" : "/palmari:off");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process p = Process.Start(psi);
                    if (p != null) p.WaitForExit(30000);
                    palmariPrima = Palmari.Attivo;

                    if (palmariPrima != chkPalmari.Checked)
                        return "il dirottamento dei palmari non e' stato applicato";
                }
                catch
                {
                    return "il dirottamento dei palmari richiede i privilegi " +
                           "di amministratore";
                }
            }

            string guaio;
            AvvioWindows.Imposta(Config.AvvioConWindows, out guaio);
            if (guaio.Length > 0) return "l'avvio con Windows non e' riuscito: " + guaio;

            return "";
        }

        private void ProvaESalva(object sender, EventArgs e)
        {
            if (txtServer.Text.Trim().Length == 0 || txtUtente.Text.Trim().Length == 0)
            {
                lblEsito.ForeColor = Color.Firebrick;
                lblEsito.Text = "Compilare server e utente.";
                return;
            }

            Config.Server = txtServer.Text.Trim();
            Config.Password = txtPassword.Text;

            // Senza dominio l'utente va qualificato col nome del server
            string utente = txtUtente.Text.Trim();
            if (utente.IndexOf('\\') < 0)
                utente = Config.Server + "\\" + utente;
            Config.Utente = utente;
            txtUtente.Text = utente;

            Config.AvvioConWindows = chkConWindows.Checked;
            Config.AvvioApplicativi = chkApplicativi.Checked;
            Config.RiavviaProxyOrderman = chkProxy.Checked;
            Config.DaAvviare.Clear();
            for (int i = 0; i < applicativi.Length && i < elencoApp.Items.Count; i++)
                if (elencoApp.GetItemChecked(i)) Config.DaAvviare.Add(applicativi[i].NomeTask);

            lblEsito.ForeColor = Color.Gray;
            lblEsito.Text = "Verifica in corso...";
            btnProva.Enabled = false;

            // La verifica gira su un thread separato: la finestra resta
            // reattiva anche se il server non risponde.
            var t = new Thread(delegate()
            {
                Remoto.Esito esito = Remoto.Connetti();

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        btnProva.Enabled = true;

                        // Le impostazioni locali si applicano comunque:
                        // riguardano questo computer, non il server, e
                        // legarle all'esito della connessione voleva
                        // dire non poterle piu' cambiare quando il
                        // server non risponde.
                        string guaio = ApplicaLocali();

                        // Si salva comunque, anche se il server non ha
                        // risposto: le impostazioni sono di questa
                        // macchina e il messaggio qui sotto promette
                        // che restano applicate. Prima si salvava solo
                        // riuscendo, e chi aveva il server spento
                        // perdeva quello che aveva appena scelto.
                        Config.Salva();

                        if (esito.Ok)
                        {
                            if (guaio.Length > 0)
                            {
                                lblEsito.ForeColor = Color.Firebrick;
                                lblEsito.Text = "Salvato, ma: " + guaio;
                                DialogResult = DialogResult.OK;
                                return;
                            }

                            DialogResult = DialogResult.OK;
                            Close();
                        }
                        else
                        {
                            lblEsito.ForeColor = Color.Firebrick;
                            string m = esito.Messaggio;
                            if (m.Length > 150) m = m.Substring(0, 150);
                            lblEsito.Text = "Connessione fallita.\n" + m +
                                            "\nLe impostazioni di questo PC sono comunque applicate.";
                        }
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    // ------------------------------------------------------------------
    // Aspetto: colori e disegno degli angoli arrotondati
    // ------------------------------------------------------------------
    public static class Stile
    {
        public static readonly Color Sfondo     = Color.FromArgb(244, 246, 248);
        public static readonly Color Testata    = Color.FromArgb(34, 48, 63);
        public static readonly Color TestataGiu = Color.FromArgb(43, 59, 78);
        public static readonly Color Scheda     = Color.White;
        public static readonly Color Bordo      = Color.FromArgb(226, 230, 235);
        public static readonly Color Testo      = Color.FromArgb(30, 41, 51);
        public static readonly Color TestoTenue = Color.FromArgb(107, 118, 132);
        public static readonly Color Verde      = Color.FromArgb(38, 148, 88);
        public static readonly Color Ambra      = Color.FromArgb(198, 132, 30);
        public static readonly Color Rosso      = Color.FromArgb(190, 76, 70);
        public static readonly Color Chiaro     = Color.FromArgb(160, 175, 195);

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

        public static Color Mescola(Color a, Color b, float quanto)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * quanto),
                (int)(a.G + (b.G - a.G) * quanto),
                (int)(a.B + (b.B - a.B) * quanto));
        }

        public static Color Schiarisci(Color c, float q) { return Mescola(c, Color.White, q); }
        public static Color Scurisci(Color c, float q)   { return Mescola(c, Color.Black, q); }
    }

    // ------------------------------------------------------------------
    // Pulsante con angoli arrotondati e reazione al passaggio del mouse
    // ------------------------------------------------------------------
    public class PulsanteTondo : Control, IButtonControl
    {
        private Color tinta;
        private bool sotto, premuto;
        public bool Contorno = false;   // variante chiara, per le azioni neutre

        public PulsanteTondo(Color colore)
        {
            tinta = colore;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        }

        protected override void OnMouseEnter(EventArgs e) { sotto = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { sotto = false; premuto = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { premuto = true;  Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)   { premuto = false; Invalidate(); base.OnMouseUp(e); }
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

            // Gli angoli scoperti devono mostrare lo sfondo del contenitore
            Color dietro = (Parent != null) ? Parent.BackColor : Stile.Sfondo;
            using (var f = new SolidBrush(dietro))
                g.FillRectangle(f, ClientRectangle);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color testo;

            using (GraphicsPath gp = Stile.Tondo(r, 6))
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
                    using (var pen = new Pen(Enabled ? Color.FromArgb(206, 212, 220)
                                                     : Color.FromArgb(228, 231, 236)))
                        g.DrawPath(pen, gp);
                    testo = Enabled ? Stile.Testo : Color.FromArgb(170, 178, 188);
                }
                else
                {
                    // Da spento si sbiadisce verso lo sfondo di chi lo
                    // contiene. Un grigio chiaro fisso andava bene sulle
                    // schede bianche, ma sulla testata scura diventava
                    // una toppa chiarissima e illeggibile.
                    Color fondo;
                    if (!Enabled)      fondo = Stile.Mescola(tinta, dietro, 0.62f);
                    else if (premuto)  fondo = Stile.Scurisci(tinta, 0.14f);
                    else if (sotto)    fondo = Stile.Schiarisci(tinta, 0.14f);
                    else               fondo = tinta;
                    using (var f = new SolidBrush(fondo)) g.FillPath(f, gp);
                    // Il riempimento lascia una cucitura piu' scura sui
                    // lati alto e sinistro: si ripassa il contorno con
                    // lo stesso colore per chiuderla.
                    using (var pen = new Pen(fondo, 1.4f)) g.DrawPath(pen, gp);
                    testo = Enabled ? Color.White : Stile.Mescola(Color.White, dietro, 0.45f);
                }
            }

            TextRenderer.DrawText(g, Text, Font, r, testo,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
        }
    }

    // ------------------------------------------------------------------
    // Riquadro bianco con angoli arrotondati
    // ------------------------------------------------------------------
    public class Riquadro : Panel
    {
        public int Raggio = 8;
        public Color Bordo = Stile.Bordo;

        public Riquadro()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = Stile.Scheda;
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
    // Etichetta di stato: pallino colorato piu' testo, su fondo tenue
    // ------------------------------------------------------------------
    public class Targhetta : Control
    {
        public Color Tinta = Stile.TestoTenue;

        public Targhetta()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        }

        public void Imposta(string testo, Color tinta)
        {
            Text = testo; Tinta = tinta; Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var f = new SolidBrush(Parent != null ? Parent.BackColor : Color.White))
                g.FillRectangle(f, ClientRectangle);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath gp = Stile.Tondo(r, Height / 2))
            using (var f = new SolidBrush(Stile.Schiarisci(Tinta, 0.86f)))
                g.FillPath(f, gp);

            int d = 8;
            int cy = (Height - d) / 2;
            using (var f = new SolidBrush(Tinta))
                g.FillEllipse(f, 11, cy, d, d);

            Rectangle rt = new Rectangle(11 + d + 6, 0, Width - (11 + d + 6) - 8, Height);
            TextRenderer.DrawText(g, Text, Font, rt, Stile.Scurisci(Tinta, 0.10f),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // ------------------------------------------------------------------
    // Finestra principale
    // ------------------------------------------------------------------
    public class FormPrincipale : Form
    {
        // ---- Applicativi gestiti -------------------------------------
        private AppInfo[] applicativi = new AppInfo[]
        {
            new AppInfo("Aera Service",
                        "Aera_Service",
                        "Aera_Service.exe",
                        @"C:\Aera\Aera_Service.exe"),

            new AppInfo("Aera Remote Function",
                        "Aera_RemoteServer",
                        "AeraRemoteServer.exe",
                        @"C:\Aera\Remote_Function\AeraRemoteServer.exe"),

            new AppInfo("Restaurant Pocket Sol",
                        "Aera_RestaurantPocket",
                        "RestaurantPocketSol.exe",
                        @"C:\Aera\RestaurantPocketSol\RestaurantPocketSol.exe"),

            // Questo e' un servizio di Windows, non un'attivita'
            // pianificata: si comanda con sc invece che con schtasks.
            new AppInfo("Orderman Classic Proxy",
                        "OrdermanClassicProxy",
                        "ClassicProxyService.exe",
                        @"C:\Program Files\Orderman\ClassicProxy\ClassicProxyService.exe",
                        true)
        };
        // --------------------------------------------------------------

        // Misure della disposizione, in un posto solo: cambiando il
        // numero di applicativi la finestra si adatta da se'.
        private const int Larghezza    = 800;
        private const int AltezzaTesta = 78;
        private const int PrimaRiga    = 92;
        private const int AltezzaRiga  = 88;
        private const int PassoRiga    = 98;
        private const int Margine      = 20;

        private Riquadro[] righe;
        private Targhetta[] targhetta;
        private Tessera[] tessere;
        private Label[] lblDettaglio;
        private PulsanteTondo[] btnAvvia;
        private PulsanteTondo[] btnFerma;
        private PulsanteTondo[] btnRiavvia;

        private PulsanteTondo btnTutti, btnRiavviaTutti, btnFermaTutti, btnAggiorna, btnConfig, btnRiconnetti;
        private Label lblServer, lblConnessione, lblAttivita;
        private TextBox txtLog;
        private System.Windows.Forms.Timer timer;
        private CheckBox chkAuto;

        // Due stati distinti: i comandi bloccano l'interfaccia, la
        // lettura periodica dello stato no. Confonderli teneva i
        // pulsanti spenti e il cursore in attesa quasi sempre.
        private volatile bool occupato = false;
        private volatile bool inLettura = false;
        private volatile bool connesso = false;

        // Ultimo stato letto, per sapere chi e' gia' attivo senza
        // interrogare di nuovo il server.
        private Dictionary<string, bool> statoNoto =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Chi non e' installato sul server: i suoi pulsanti restano
        // spenti anche quando si riabilitano tutti gli altri.
        private bool[] assente;

        public FormPrincipale()
        {
            Text = "AeraControl  " + Versione.Numero;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = Stile.Sfondo;
            DoubleBuffered = true;
            Icone.Applica(this);

            int yPiede = PrimaRiga + applicativi.Length * PassoRiga + 8;
            // ultima voce: 22 per la riga in calce
            ClientSize = new Size(Larghezza, yPiede + 40 + 14 + 132 + 22 + 14);

            CostruisciIntestazione();
            CostruisciRighe();
            CostruisciPiede(yPiede);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 10000;
            timer.Tick += delegate
            {
                if (chkAuto.Checked && !occupato && !inLettura && connesso) Aggiorna(false);
            };

            Shown += AlCaricamento;
            FormClosing += delegate
            {
                timer.Stop();
                var t = new Thread(delegate() { Remoto.Disconnetti(); });
                t.IsBackground = true;
                t.Start();
            };
        }

        // -------------------------------------------------- intestazione
        private void CostruisciIntestazione()
        {
            var testata = new Panel();
            testata.Location = new Point(0, 0);
            testata.Size = new Size(Larghezza, AltezzaTesta);
            // Tinta piatta e non sfumata: le etichette trasparenti e gli
            // angoli dei pulsanti ridisegnano il proprio fondo con il
            // colore del pannello, non con quello effettivamente
            // dipinto. Con una sfumatura i due non coincidono e
            // restano delle toppe chiare quando il testo cambia.
            testata.BackColor = Stile.Testata;
            Controls.Add(testata);

            var titolo = new Label();
            titolo.Text = "AeraControl";
            titolo.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            titolo.ForeColor = Color.White;
            titolo.BackColor = Color.Transparent;
            titolo.Location = new Point(Margine, 12);
            titolo.Size = new Size(320, 28);
            testata.Controls.Add(titolo);

            lblServer = new Label();
            lblServer.Text = "server non configurato";
            lblServer.ForeColor = Stile.Chiaro;
            lblServer.BackColor = Color.Transparent;
            lblServer.Font = new Font("Segoe UI", 8.5F);
            lblServer.Location = new Point(Margine + 1, 44);
            lblServer.Size = new Size(300, 18);
            testata.Controls.Add(lblServer);

            // I due pulsanti stanno in alto a destra, lo stato sotto:
            // prima si sovrapponevano.
            btnConfig = new PulsanteTondo(Color.FromArgb(64, 82, 104));
            btnConfig.Text = "Configura";
            btnConfig.Size = new Size(104, 30);
            btnConfig.Location = new Point(Larghezza - Margine - 104, 13);
            btnConfig.Click += ApriConfigurazione;
            testata.Controls.Add(btnConfig);

            btnRiconnetti = new PulsanteTondo(Color.FromArgb(64, 82, 104));
            btnRiconnetti.Text = "Riconnetti";
            btnRiconnetti.Size = new Size(104, 30);
            btnRiconnetti.Location = new Point(Larghezza - Margine - 104 - 8 - 104, 13);
            btnRiconnetti.Click += delegate { Connetti(); };
            testata.Controls.Add(btnRiconnetti);

            lblConnessione = new Label();
            lblConnessione.Text = "non connesso";
            lblConnessione.TextAlign = ContentAlignment.MiddleRight;
            lblConnessione.ForeColor = Color.FromArgb(235, 175, 95);
            lblConnessione.BackColor = Color.Transparent;
            lblConnessione.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            lblConnessione.Location = new Point(Larghezza - Margine - 260, 44);
            lblConnessione.Size = new Size(260, 18);
            testata.Controls.Add(lblConnessione);

            // Segnalatore discreto al posto del cursore di attesa
            lblAttivita = new Label();
            lblAttivita.Text = "";
            lblAttivita.TextAlign = ContentAlignment.MiddleRight;
            lblAttivita.ForeColor = Stile.Chiaro;
            lblAttivita.BackColor = Color.Transparent;
            lblAttivita.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
            // Fra il nome del server e lo stato della connessione,
            // senza accavallarsi ne' all'uno ne' all'altro.
            lblAttivita.Location = new Point(Margine + 306, 44);
            lblAttivita.Size = new Size(Larghezza - Margine - 260 - (Margine + 306) - 10, 18);
            testata.Controls.Add(lblAttivita);
        }

        // --------------------------------------------------- righe app
        private void CostruisciRighe()
        {
            int n = applicativi.Length;
            righe = new Riquadro[n];
            targhetta = new Targhetta[n];
            tessere = new Tessera[n];
            lblDettaglio = new Label[n];
            btnAvvia = new PulsanteTondo[n];
            btnFerma = new PulsanteTondo[n];
            btnRiavvia = new PulsanteTondo[n];
            assente = new bool[n];

            // Tinte di ripiego per le tessere con le iniziali, usate
            // finche' non si riescono a leggere le icone vere.
            Color[] tinte = new Color[] {
                Color.FromArgb(46, 110, 180), Color.FromArgb(52, 140, 110),
                Color.FromArgb(150, 90, 170), Color.FromArgb(190, 120, 50),
                Color.FromArgb(160, 70, 90) };

            int larghezzaScheda = Larghezza - Margine * 2;
            int largPuls = 78, altPuls = 32, spazio = 8;
            int xFerma   = larghezzaScheda - 18 - largPuls;
            int xRiavvia = xFerma - spazio - largPuls;
            int xAvvia   = xRiavvia - spazio - largPuls;
            int yPuls    = (AltezzaRiga - altPuls) / 2;

            int largTarga = 104;
            int xTarga = xAvvia - spazio - largTarga;
            int xTesto = 64;
            int largTesto = xTarga - xTesto - 12;

            for (int i = 0; i < n; i++)
            {
                int idx = i;

                var p = new Riquadro();
                p.Location = new Point(Margine, PrimaRiga + i * PassoRiga);
                p.Size = new Size(larghezzaScheda, AltezzaRiga);
                Controls.Add(p);
                righe[i] = p;

                var ts = new Tessera();
                ts.Location = new Point(16, (AltezzaRiga - 36) / 2);
                ts.Size = new Size(36, 36);
                ts.Iniziali = Iniziali(applicativi[i].Titolo);
                ts.Tinta = tinte[i % tinte.Length];
                ts.Immagine = Icone.Migliore(applicativi[i].NomeTask);
                p.Controls.Add(ts);
                tessere[i] = ts;

                var t = new Targhetta();
                t.Location = new Point(xTarga, (AltezzaRiga - 24) / 2);
                t.Size = new Size(largTarga, 24);
                t.Imposta("ignoto", Stile.TestoTenue);
                p.Controls.Add(t);
                targhetta[i] = t;

                var nome = new Label();
                nome.Text = applicativi[i].Titolo;
                nome.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
                nome.ForeColor = Stile.Testo;
                nome.Location = new Point(xTesto, 14);
                nome.Size = new Size(largTesto, 22);
                p.Controls.Add(nome);

                var percorso = new Label();
                percorso.Text = applicativi[i].Percorso;
                percorso.ForeColor = Stile.TestoTenue;
                percorso.Font = new Font("Segoe UI", 8.5F);
                percorso.AutoEllipsis = true;
                percorso.Location = new Point(xTesto, 39);
                percorso.Size = new Size(largTesto, 17);
                p.Controls.Add(percorso);

                var dett = new Label();
                dett.Text = "stato non disponibile";
                dett.ForeColor = Stile.TestoTenue;
                dett.Font = new Font("Segoe UI", 8.5F);
                dett.AutoEllipsis = true;
                dett.Location = new Point(xTesto, 57);
                dett.Size = new Size(largTesto, 17);
                p.Controls.Add(dett);
                lblDettaglio[i] = dett;

                btnAvvia[i] = new PulsanteTondo(Stile.Verde);
                btnAvvia[i].Text = "Avvia";
                btnAvvia[i].Location = new Point(xAvvia, yPuls);
                btnAvvia[i].Size = new Size(largPuls, altPuls);
                btnAvvia[i].Click += delegate { AzioneSingola(idx, "avvia"); };
                p.Controls.Add(btnAvvia[i]);

                btnRiavvia[i] = new PulsanteTondo(Stile.Ambra);
                btnRiavvia[i].Text = "Riavvia";
                btnRiavvia[i].Location = new Point(xRiavvia, yPuls);
                btnRiavvia[i].Size = new Size(largPuls, altPuls);
                btnRiavvia[i].Click += delegate { AzioneSingola(idx, "riavvia"); };
                p.Controls.Add(btnRiavvia[i]);

                btnFerma[i] = new PulsanteTondo(Stile.Rosso);
                btnFerma[i].Text = "Ferma";
                btnFerma[i].Location = new Point(xFerma, yPuls);
                btnFerma[i].Size = new Size(largPuls, altPuls);
                btnFerma[i].Click += delegate { AzioneSingola(idx, "ferma"); };
                p.Controls.Add(btnFerma[i]);
            }
        }

        private static string Iniziali(string titolo)
        {
            var pezzi = titolo.Split(new char[] { ' ', '-', '_' },
                                     StringSplitOptions.RemoveEmptyEntries);
            string s = "";
            foreach (string p in pezzi)
            {
                if (s.Length >= 2) break;
                s += char.ToUpperInvariant(p[0]);
            }
            return (s.Length > 0) ? s : "?";
        }

        // Le icone vere stanno negli eseguibili sul server: si leggono
        // una volta dopo la connessione e restano salvate per le volte
        // successive.
        private void CercaIcone()
        {
            if (!Icone.Scarica(applicativi)) return;

            SulThreadUI(delegate
            {
                for (int i = 0; i < applicativi.Length; i++)
                {
                    Image im = Icone.DaCache(applicativi[i].NomeTask);
                    if (im == null) continue;
                    if (tessere[i].Immagine != null) tessere[i].Immagine.Dispose();
                    tessere[i].Immagine = im;
                    tessere[i].Invalidate();
                }
            });
        }

        // -------------------------------------------------------- piede
        private void CostruisciPiede(int y)
        {
            int largo = 128, passo = 136;

            btnTutti = new PulsanteTondo(Stile.Verde);
            btnTutti.Text = "Avvia tutti";
            btnTutti.Location = new Point(Margine, y);
            btnTutti.Size = new Size(largo, 40);
            btnTutti.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnTutti.Click += delegate { AzioneTutti("avvia"); };
            Controls.Add(btnTutti);

            btnRiavviaTutti = new PulsanteTondo(Stile.Ambra);
            btnRiavviaTutti.Text = "Riavvia tutti";
            btnRiavviaTutti.Location = new Point(Margine + passo, y);
            btnRiavviaTutti.Size = new Size(largo, 40);
            btnRiavviaTutti.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnRiavviaTutti.Click += delegate { AzioneTutti("riavvia"); };
            Controls.Add(btnRiavviaTutti);

            btnFermaTutti = new PulsanteTondo(Stile.Rosso);
            btnFermaTutti.Text = "Ferma tutti";
            btnFermaTutti.Location = new Point(Margine + passo * 2, y);
            btnFermaTutti.Size = new Size(largo, 40);
            btnFermaTutti.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnFermaTutti.Click += delegate { AzioneTutti("ferma"); };
            Controls.Add(btnFermaTutti);

            var b = new PulsanteTondo(Color.White);
            b.Contorno = true;
            b.Text = "Aggiorna";
            b.Location = new Point(Margine + passo * 3, y);
            b.Size = new Size(104, 40);
            b.Click += delegate { Aggiorna(true); };
            btnAggiorna = b;
            Controls.Add(btnAggiorna);

            chkAuto = new CheckBox();
            chkAuto.Text = "Aggiornamento automatico";
            chkAuto.Checked = true;
            chkAuto.ForeColor = Stile.Testo;
            chkAuto.BackColor = Color.Transparent;
            chkAuto.Location = new Point(Margine + passo * 3 + 116, y + 11);
            chkAuto.Size = new Size(200, 20);
            Controls.Add(chkAuto);

            // Il registro sta dentro un riquadro arrotondato: una
            // TextBox da sola non li disegna.
            var cornice = new Riquadro();
            cornice.Location = new Point(Margine, y + 54);
            cornice.Size = new Size(Larghezza - Margine * 2, 132);
            Controls.Add(cornice);

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.Location = new Point(12, 10);
            txtLog.Size = new Size(cornice.Width - 24, 112);
            txtLog.BackColor = Color.White;
            txtLog.ForeColor = Color.FromArgb(60, 72, 86);
            txtLog.Font = new Font("Consolas", 8.5F);
            cornice.Controls.Add(txtLog);

            // In calce
            var firma = new Label();
            firma.Text = "IOTATEC srl";
            firma.Font = new Font("Segoe UI", 8F);
            firma.ForeColor = Stile.TestoTenue;
            firma.BackColor = Color.Transparent;
            firma.TextAlign = ContentAlignment.MiddleLeft;
            firma.Location = new Point(Margine + 2, cornice.Bottom + 5);
            firma.Size = new Size(200, 16);
            Controls.Add(firma);

            var vers = new Label();
            vers.Text = "versione " + Versione.Numero;
            vers.Font = new Font("Segoe UI", 8F);
            vers.ForeColor = Stile.TestoTenue;
            vers.BackColor = Color.Transparent;
            vers.TextAlign = ContentAlignment.MiddleRight;
            vers.Location = new Point(Larghezza - Margine - 200 - 2, cornice.Bottom + 5);
            vers.Size = new Size(200, 16);
            Controls.Add(vers);
        }

        // ------------------------------------------------------- avvio
        private void AlCaricamento(object sender, EventArgs e)
        {
            Refresh();
            Log("Applicazione avviata.");

            Config.Carica();

            // Se il dirottamento dei palmari e' acceso, il
            // sincronizzatore deve girare: tiene la spia di
            // AeraRestaurant allineata a cio' che c'e' sul server.
            Palmari.AssicuraSincronizzatore();

            if (!Config.Esiste || Config.Server.Length == 0)
            {
                Log("Nessuna configurazione trovata.");
                StatoConnessione(false, "da configurare");
                ApriConfigurazione(null, null);
                return;
            }

            Log("Configurazione: " + Config.Utente);
            lblServer.Text = Config.Server + "  -  " + Config.Utente;
            Connetti();
        }

        private void ApriConfigurazione(object sender, EventArgs e)
        {
            timer.Stop();

            using (var f = new FormConfig(applicativi))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    lblServer.Text = Config.Server + "  -  " + Config.Utente;
                    Log("Configurazione salvata.");

                    Remoto.TasklistDisponibile = true;
                    connesso = true;
                    StatoConnessione(true, "connesso");
                    Aggiorna(true);
                    timer.Start();
                }
                else
                {
                    Log("Configurazione annullata.");
                }
            }
        }

        // -------------------------------------------------- connessione
        private void Connetti()
        {
            if (occupato) return;

            if (Config.Server.Length == 0)
            {
                Log("Server non configurato: premere Configura.");
                return;
            }

            timer.Stop();
            connesso = false;
            StatoConnessione(false, "connessione in corso...");
            Log("Connessione a " + Config.Server + " ...");

            InBackground(delegate
            {
                Remoto.Esito esito = Remoto.Connetti();

                SulThreadUI(delegate
                {
                    if (esito.Ok)
                    {
                        connesso = true;
                        Remoto.TasklistDisponibile = true;
                        Log("Connesso.");
                        StatoConnessione(true, "connesso");
                        timer.Start();
                    }
                    else
                    {
                        connesso = false;
                        Log("Connessione fallita: " + esito.Messaggio);
                        StatoConnessione(false, "non connesso");
                    }
                });

                if (esito.Ok)
                {
                    LeggiStatoOra(true);
                    CercaIcone();
                    if (Config.AvvioApplicativi) AvviaQuelliScelti();
                }
            });
        }

        // Gira sul thread di lavoro, subito dopo la prima lettura:
        // si avviano solo quelli scelti e solo se risultano fermi,
        // cosi' riaprendo il programma non si riavvia niente per
        // sbaglio.
        private void AvviaQuelliScelti()
        {
            var daFare = new List<int>();

            lock (statoNoto)
            {
                for (int i = 0; i < applicativi.Length; i++)
                {
                    if (!Config.DaAvviare.Contains(applicativi[i].NomeTask)) continue;
                    if (statoNoto.ContainsKey(applicativi[i].NomeTask) &&
                        statoNoto[applicativi[i].NomeTask]) continue;
                    daFare.Add(i);
                }
            }

            if (daFare.Count == 0)
            {
                SulThreadUI(delegate { Log("Avvio automatico: erano gia' tutti attivi."); });
                return;
            }

            SulThreadUI(delegate { Log("--- avvio automatico ---"); });

            foreach (int i in daFare)
            {
                EseguiAzione(i, "avvia");
                Thread.Sleep(1200);
            }

            Thread.Sleep(2000);
            LeggiStatoOra(false);
        }

        // ----------------------------------------------------- aggiorna
        private void Aggiorna(bool verboso)
        {
            if (occupato || !connesso) return;
            InBackgroundLeggero(delegate { LeggiStatoOra(verboso); });
        }

        // Gira sempre su thread di lavoro
        private void LeggiStatoOra(bool verboso)
        {
            string errore;
            bool dettagli;
            Dictionary<string, StatoApp> stato =
                Remoto.LeggiStato(applicativi, out errore, out dettagli);

            foreach (AppInfo a in applicativi)
            {
                bool attivo = stato.ContainsKey(a.NomeTask) && stato[a.NomeTask].InEsecuzione;
                lock (statoNoto) statoNoto[a.NomeTask] = attivo;
            }

            SulThreadUI(delegate
            {
                if (errore.Length > 0) Log(errore);

                for (int i = 0; i < applicativi.Length; i++)
                {
                    StatoApp s = null;
                    if (stato.ContainsKey(applicativi[i].NomeTask))
                        s = stato[applicativi[i].NomeTask];
                    ImpostaRiga(i, s);
                }

                if (verboso) Log("Stato aggiornato.");
            });
        }

        private void ImpostaRiga(int i, StatoApp s)
        {
            // Si riparte sempre da "presente": lo stato puo' cambiare
            // fra una lettura e l'altra.
            if (s == null || !s.NonInstallato) assente[i] = false;

            if (s != null && s.InEsecuzione)
            {
                // "in esecuzione" non ci sta e veniva troncato
                targhetta[i].Imposta("attivo", Stile.Verde);
                righe[i].Bordo = Stile.Schiarisci(Stile.Verde, 0.62f);
                lblDettaglio[i].ForeColor = Stile.Verde;

                if (s.DettagliDisponibili)
                {
                    lblDettaglio[i].Text = string.Format(
                        "PID {0}  -  sessione {1}  -  {2}",
                        s.Pid, s.Sessione, s.Memoria);
                }
                else
                {
                    lblDettaglio[i].Text = "attivo sul server";
                }
            }
            else if (s != null && s.NonInstallato)
            {
                // Non c'e' proprio su quel server: va distinto da
                // "fermo", perche' non c'e' niente da avviare.
                targhetta[i].Imposta("assente", Stile.TestoTenue);
                righe[i].Bordo = Stile.Bordo;
                lblDettaglio[i].ForeColor = Stile.TestoTenue;
                lblDettaglio[i].Text = "non installato su questo server";
                assente[i] = true;
                btnAvvia[i].Enabled = false;
                btnFerma[i].Enabled = false;
                btnRiavvia[i].Enabled = false;
            }
            else
            {
                bool ignoto = (s == null);
                targhetta[i].Imposta(ignoto ? "ignoto" : "fermo", Stile.TestoTenue);
                righe[i].Bordo = Stile.Bordo;
                lblDettaglio[i].ForeColor = Stile.TestoTenue;
                lblDettaglio[i].Text = ignoto ? "stato non disponibile"
                                              : "non in esecuzione";
            }
            righe[i].Invalidate();
        }

        // ------------------------------------------------------ azioni
        private void AzioneSingola(int i, string tipo)
        {
            if (occupato || !connesso) return;

            if (tipo != "avvia")
            {
                string domanda = (tipo == "ferma")
                    ? "Terminare " + applicativi[i].Titolo + " sul server?"
                    : "Riavviare " + applicativi[i].Titolo + "?";

                if (MessageBox.Show(domanda, "Conferma",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    != DialogResult.Yes) return;
            }

            int indice = i;
            string azione = tipo;

            InBackground(delegate
            {
                EseguiAzione(indice, azione);
                Thread.Sleep(2000);
                LeggiStatoOra(false);
            });
        }

        private void AzioneTutti(string tipo)
        {
            if (occupato || !connesso) return;

            if (tipo == "riavvia" || tipo == "ferma")
            {
                string domanda = (tipo == "ferma")
                    ? "Fermare tutti gli applicativi sul server?"
                    : "Riavviare tutti gli applicativi?";

                if (MessageBox.Show(domanda, "Conferma",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    != DialogResult.Yes) return;
            }

            string azione = tipo;

            InBackground(delegate
            {
                SulThreadUI(delegate { Log("--- " + azione + " tutti ---"); });

                if (azione == "riavvia" || azione == "ferma")
                {
                    // Si spegne in ordine inverso: l'ultimo ad essere
                    // avviato e' il primo a fermarsi.
                    for (int i = applicativi.Length - 1; i >= 0; i--)
                    {
                        EseguiAzione(i, "ferma");
                        Thread.Sleep(600);
                    }
                    if (azione == "ferma")
                    {
                        Thread.Sleep(2000);
                        LeggiStatoOra(false);
                        SulThreadUI(delegate { Log("--- operazione completata ---"); });
                        return;
                    }
                    Thread.Sleep(3000);
                }

                for (int i = 0; i < applicativi.Length; i++)
                {
                    EseguiAzione(i, "avvia");
                    Thread.Sleep(1200);
                }

                Thread.Sleep(2500);
                LeggiStatoOra(false);
                SulThreadUI(delegate { Log("--- operazione completata ---"); });
            });
        }

        // Gira sempre su thread di lavoro
        private void EseguiAzione(int i, string tipo)
        {
            AppInfo app = applicativi[i];
            Remoto.Esito esito;
            string verbo;

            if (tipo == "ferma")
            {
                esito = Remoto.Ferma(app);
                verbo = "Arresto";
                // Se qui c'e' un segnaposto acceso per la spia di
                // AeraRestaurant, va spento insieme all'applicativo.
                Palmari.SpegniSegnaposto(app.NomeTask);
            }
            else if (tipo == "riavvia")
            {
                Remoto.Ferma(app);
                // Un servizio ci mette di piu' a chiudersi davvero, e
                // riavviarlo troppo presto fallisce.
                Thread.Sleep(app.EServizio ? 5000 : 2500);
                esito = Remoto.Avvia(app);
                verbo = "Riavvio";
            }
            else
            {
                esito = Remoto.Avvia(app);
                verbo = "Avvio";
            }

            Remoto.Esito e = esito;
            string v = verbo;
            string titolo = app.Titolo;

            SulThreadUI(delegate
            {
                if (e.Ok) Log(v + " richiesto: " + titolo);
                else Log(v + " FALLITO " + titolo + " -> " + e.Messaggio);
            });
        }

        // ---------------------------------------------------- utilita'
        // Esegue il lavoro su un thread separato: l'interfaccia resta
        // sempre reattiva, anche se il server non risponde.
        private void InBackground(ThreadStart lavoro)
        {
            if (occupato) return;
            occupato = true;
            AbilitaComandi(false);

            var t = new Thread(delegate()
            {
                try { lavoro(); }
                catch (Exception ex)
                {
                    string m = ex.Message;
                    SulThreadUI(delegate { Log("Errore interno: " + m); });
                }
                finally
                {
                    SulThreadUI(delegate
                    {
                        occupato = false;
                        AbilitaComandi(true);
                    });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // Per la sola lettura dello stato: non spegne i pulsanti e non
        // tocca il cursore. Prima ogni giro automatico bloccava tutto,
        // e siccome durava piu' dell'intervallo l'interfaccia restava
        // perennemente in attesa.
        private void InBackgroundLeggero(ThreadStart lavoro)
        {
            if (occupato || inLettura) return;
            inLettura = true;
            SulThreadUI(delegate { lblAttivita.Text = "aggiornamento..."; });

            var t = new Thread(delegate()
            {
                try { lavoro(); }
                catch (Exception ex)
                {
                    string m = ex.Message;
                    SulThreadUI(delegate { Log("Errore interno: " + m); });
                }
                finally
                {
                    SulThreadUI(delegate
                    {
                        inLettura = false;
                        lblAttivita.Text = "";
                    });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void SulThreadUI(MethodInvoker azione)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(azione);
                else azione();
            }
            catch { }
        }

        private void AbilitaComandi(bool abilita)
        {
            btnTutti.Enabled = abilita;
            btnRiavviaTutti.Enabled = abilita;
            btnFermaTutti.Enabled = abilita;
            btnAggiorna.Enabled = abilita;
            btnRiconnetti.Enabled = abilita;

            for (int i = 0; i < applicativi.Length; i++)
            {
                bool ok = abilita && !assente[i];
                btnAvvia[i].Enabled = ok;
                btnFerma[i].Enabled = ok;
                btnRiavvia[i].Enabled = ok;
            }

            // Niente cursore di attesa: i pulsanti spenti e la scritta
            // in alto bastano a dire che c'e' un comando in corso, e la
            // finestra resta utilizzabile.
            lblAttivita.Text = abilita ? "" : "comando in corso...";
        }

        private void StatoConnessione(bool ok, string testo)
        {
            lblConnessione.Text = testo;
            lblConnessione.ForeColor = ok
                ? Color.FromArgb(120, 220, 140)
                : Color.FromArgb(235, 175, 95);

            if (!ok)
            {
                for (int i = 0; i < applicativi.Length; i++)
                    ImpostaRiga(i, null);
            }
        }

        private void Log(string testo)
        {
            if (txtLog == null) return;
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " +
                              testo + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }
    }

    // ------------------------------------------------------------------
    static class Programma
    {
        // Senza questa dichiarazione Windows ingrandisce la finestra
        // per conto suo sugli schermi ad alta risoluzione, e il
        // risultato e' sfocato. Va chiamata prima di creare finestre.
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] argomenti)
        {
            // Segnaposto: questo stesso programma, copiato col nome di
            // un applicativo, tenuto acceso solo perche' AeraRestaurant
            // veda il processo. Si esce subito da qui per non caricare
            // niente dell'interfaccia.
            foreach (string a in argomenti)
            {
                if (!string.Equals(a, "/presenza", StringComparison.OrdinalIgnoreCase))
                    continue;
                while (true) Thread.Sleep(60000);
            }

            // Sincronizzatore: tiene i segnaposto allineati a cio' che
            // gira sul server, senza aprire nessuna finestra.
            foreach (string a in argomenti)
            {
                if (!string.Equals(a, "/sincronizza", StringComparison.OrdinalIgnoreCase))
                    continue;
                Palmari.SincronizzaSempre();
                return;
            }

            try
            {
                if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Chiamato al posto di un applicativo dei palmari: si fa da
            // intermediario e si esce, senza aprire la console.
            if (Palmari.ChiamatoAlPostoDi(argomenti))
            {
                Palmari.Dirotta(argomenti);
                return;
            }

            // Attivazione o rimozione del dirottamento: arriva da un
            // rilancio con privilegi di amministratore, perche' la
            // chiave sta in HKLM.
            foreach (string a in argomenti)
            {
                if (!a.StartsWith("/palmari:", StringComparison.OrdinalIgnoreCase)) continue;

                bool acceso = a.EndsWith(":on", StringComparison.OrdinalIgnoreCase);
                string guaio;
                bool ok = Palmari.Imposta(acceso, out guaio);

                if (!ok)
                    MessageBox.Show("Non sono riuscito a " +
                        (acceso ? "attivare" : "rimuovere") +
                        " il dirottamento del pulsante Palmari.\n\n" + guaio,
                        "Palmari", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool installazioneRichiesta = false;
            foreach (string a in argomenti)
            {
                if (string.Equals(a, "/installa", StringComparison.OrdinalIgnoreCase))
                    installazioneRichiesta = true;
            }

            if (installazioneRichiesta)
            {
                try
                {
                    Installazione.Installa();
                    MessageBox.Show(
                        "Applicazione installata in:\n" + Installazione.Cartella +
                        "\n\nCollegamento creato sul desktop e nel menu Start.",
                        "Installazione completata",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);

                    Process.Start(Installazione.PercorsoInstallato);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Installazione non riuscita.\n\n" + ex.Message,
                        "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            // La domanda va fatta una volta sola. Prima dipendeva solo
            // da dove si trovava l'eseguibile: tenendolo sul desktop
            // invece che nella cartella d'installazione tornava a ogni
            // avvio. Ora si tace anche se l'installazione c'e' gia' o
            // se e' stata rifiutata in passato.
            Config.Carica();

            bool giaInstallato = false;
            try { giaInstallato = File.Exists(Installazione.PercorsoInstallato); }
            catch { }

            if (!Installazione.GiaInPosizione && !giaInstallato && !Config.NienteInstallazione)
            {
                DialogResult scelta = MessageBox.Show(
                    "Installare l'applicazione in:\n" + Installazione.Cartella +
                    "\n\nVerra' creato il collegamento sul desktop e nel menu Start." +
                    "\n\nRispondendo No l'applicazione funziona comunque da qui, " +
                    "senza collegamenti.",
                    "Console applicativi Aera",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (scelta == DialogResult.Yes)
                {
                    if (Installazione.Amministratore)
                    {
                        try
                        {
                            Installazione.Installa();
                            MessageBox.Show(
                                "Applicazione installata in:\n" + Installazione.Cartella +
                                "\n\nCollegamento creato sul desktop e nel menu Start.",
                                "Installazione completata",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);

                            Process.Start(Installazione.PercorsoInstallato);
                            return;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                "Installazione non riuscita.\n\n" + ex.Message +
                                "\n\nL'applicazione verra' avviata da questa cartella.",
                                "Errore", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    else
                    {
                        try
                        {
                            var psi = new ProcessStartInfo(
                                          Installazione.PercorsoCorrente, "/installa");
                            psi.UseShellExecute = true;
                            psi.Verb = "runas";
                            Process.Start(psi);
                            return;
                        }
                        catch
                        {
                            MessageBox.Show(
                                "Elevazione annullata.\n\n" +
                                "L'applicazione verra' avviata da questa cartella.",
                                "Installazione", MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                    }
                }
                else
                {
                    // Rifiutata: non si torna a chiedere ai prossimi avvii.
                    try
                    {
                        Config.NienteInstallazione = true;
                        Config.Salva();
                    }
                    catch { }
                }
            }
            else if (Installazione.GiaInPosizione && !Installazione.CollegamentoPresente)
            {
                try { Installazione.CreaCollegamenti(); }
                catch { }
            }

            Application.Run(new FormPrincipale());
        }
    }
}

