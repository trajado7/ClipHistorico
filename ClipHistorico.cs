// =====================================================================
//  ClipHistorico.cs
//  Area de transferencia turbinada para Windows
//
//  Abas: Historico (textos e imagens copiados), Salvos (seus textos fixos)
//  e Emotes coloridos (com busca em portugues, como o Win + . do Windows).
//
//  Atalhos padrao (mude na engrenagem da janela):
//    Win + V ............ historico
//    Win + . ............ emotes
//    Ctrl + Alt + J ..... print da janela ativa (funciona dentro de jogos)
//    Ctrl + Alt + K ..... print da tela inteira
//    Win + Shift + S .... recortar parte da tela (funciona em tela cheia)
//
//  Compilar: rode o compilar.bat (usa o compilador do proprio Windows).
//  Escrito em C# 5 para funcionar com o csc.exe do .NET Framework 4.
// =====================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;


// ---------------------------------------------------------------------
// 1. CONFIGURACOES
// ---------------------------------------------------------------------

// Uma combinacao de teclas. Os valores dos modificadores sao os mesmos do Windows.
struct Atalho
{
    public const int Alt = 1, Ctrl = 2, Shift = 4, Win = 8;

    public readonly int Mods;
    public readonly int Vk;

    public Atalho(int mods, int vk) { Mods = mods; Vk = vk; }

    public bool Vazio { get { return Vk == 0; } }
    public bool Igual(Atalho o) { return Mods == o.Mods && Vk == o.Vk; }

    public override string ToString()
    {
        if (Vazio) return "Nenhum";
        StringBuilder sb = new StringBuilder();
        if ((Mods & Win) != 0)   sb.Append("Win + ");
        if ((Mods & Ctrl) != 0)  sb.Append("Ctrl + ");
        if ((Mods & Alt) != 0)   sb.Append("Alt + ");
        if ((Mods & Shift) != 0) sb.Append("Shift + ");
        sb.Append(NomeTecla(Vk));
        return sb.ToString();
    }

    public static string NomeTecla(int vk)
    {
        if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
        if (vk >= 0x60 && vk <= 0x69) return "Num " + (vk - 0x60);
        switch (vk)
        {
            case 0xBE: return ".";
            case 0xBC: return ",";
            case 0xBD: return "-";
            case 0xBB: return "=";
            case 0xBF: return ";";
            case 0xC1: return "/";
            case 0x20: return "Espa\u00e7o";
            case 0x0D: return "Enter";
            case 0x09: return "Tab";
            case 0x2C: return "PrintScreen";
            case 0x13: return "Pause";
            case 0x91: return "ScrollLock";
            case 0x2D: return "Insert";
            case 0x2E: return "Delete";
            case 0x24: return "Home";
            case 0x23: return "End";
            case 0x21: return "PageUp";
            case 0x22: return "PageDown";
            case 0x25: return "Esquerda";
            case 0x26: return "Cima";
            case 0x27: return "Direita";
            case 0x28: return "Baixo";
        }
        return ((Keys)vk).ToString();
    }

    // Teclas que podem ser usadas sozinhas, sem Ctrl/Alt/Shift/Win
    public static bool PodeSozinha(int vk)
    {
        return (vk >= 0x70 && vk <= 0x87) || vk == 0x2C || vk == 0x13 || vk == 0x91;
    }
}

static class Config
{
    // Limites do historico
    public const int MaxTextos      = 50;
    public const int MaxImagens     = 10;
    public const int TextoEmMemoria = 4096;   // textos maiores ficam so no disco
    public const int MaxRecentes    = 32;     // emotes recentes

    // Pastas
    public static readonly string Pasta = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ClipHistorico");      // historico
    public static readonly string PastaPrints = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PRINTS");            // prints
    public static readonly string PastaDados = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipHistorico"); // config, salvos, recentes
                                                                                                // (o "Limpar tudo" nao mexe aqui)
    // Opcoes (mudam pela engrenagem)
    public static bool ColarAuto     = false;
    public static bool SomAoCapturar = true;

    // Prazo do historico em horas (0 = nunca apaga por tempo)
    public static int PrazoHoras = 24;
    public static readonly int[] Prazos = { 1, 12, 24, 168, 720, 0 };
    public static readonly string[] NomesPrazos = { "1 hora", "12 horas", "1 dia", "7 dias", "30 dias", "Nunca" };

    // Acoes que tem atalho
    public const int AbrirHistorico = 0, AbrirEmotes = 1, PrintJanela = 2, PrintTela = 3, RecortarTela = 4;

    public static readonly string[] NomesAcoes =
    {
        "Abrir a \u00e1rea de transfer\u00eancia",
        "Abrir os emotes",
        "Print da janela ativa",
        "Print da tela inteira",
        "Recortar parte da tela"
    };

    public static readonly Atalho[] Padrao =
    {
        new Atalho(Atalho.Win, 0x56),                 // Win + V
        new Atalho(Atalho.Win, 0xBE),                 // Win + .
        new Atalho(Atalho.Ctrl | Atalho.Alt, 0x4A),   // Ctrl + Alt + J
        new Atalho(Atalho.Ctrl | Atalho.Alt, 0x4B),   // Ctrl + Alt + K
        new Atalho(Atalho.Win | Atalho.Shift, 0x53)   // Win + Shift + S
    };

    public static Atalho[] Atalhos = (Atalho[])Padrao.Clone();

    static string Arquivo { get { return Path.Combine(PastaDados, "config.ini"); } }

    public static void Carregar()
    {
        try
        {
            if (!File.Exists(Arquivo)) return;
            foreach (string linha in File.ReadAllLines(Arquivo))
            {
                int igual = linha.IndexOf('=');
                if (igual <= 0) continue;
                string chave = linha.Substring(0, igual).Trim();
                string valor = linha.Substring(igual + 1).Trim();

                if (chave == "ColarAuto") ColarAuto = valor == "1";
                else if (chave == "Som") SomAoCapturar = valor == "1";
                else if (chave == "PrazoHoras")
                {
                    int h;
                    if (int.TryParse(valor, out h) && h >= 0) PrazoHoras = h;
                }
                else if (chave.StartsWith("Atalho"))
                {
                    int i, mods, vk;
                    string[] p = valor.Split(',');
                    if (int.TryParse(chave.Substring(6), out i) && i >= 0 && i < Atalhos.Length &&
                        p.Length == 2 && int.TryParse(p[0], out mods) && int.TryParse(p[1], out vk))
                        Atalhos[i] = new Atalho(mods, vk);
                }
            }
        }
        catch (Exception ex) { App.Log("Erro ao ler config: " + ex.Message); }
    }

    public static void Salvar()
    {
        try
        {
            Directory.CreateDirectory(PastaDados);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ColarAuto=" + (ColarAuto ? "1" : "0"));
            sb.AppendLine("Som=" + (SomAoCapturar ? "1" : "0"));
            sb.AppendLine("PrazoHoras=" + PrazoHoras);
            for (int i = 0; i < Atalhos.Length; i++)
                sb.AppendLine("Atalho" + i + "=" + Atalhos[i].Mods + "," + Atalhos[i].Vk);
            File.WriteAllText(Arquivo, sb.ToString());
        }
        catch (Exception ex) { App.Log("Erro ao salvar config: " + ex.Message); }
    }
}


// ---------------------------------------------------------------------
// 2. FUNCOES DO WINDOWS
// ---------------------------------------------------------------------
static class Nativo
{
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mod, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessageTexto(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(IntPtr hWnd, string app, string idList);

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hHook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hHook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("winmm.dll")] public static extern bool PlaySound(IntPtr som, IntPtr modulo, uint opcoes);
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] public static extern void CopiarMemoria(IntPtr destino, IntPtr origem, IntPtr bytes);

    // Laco de mensagens proprio do gancho de teclado
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint thread, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr hProcess);

    // Usados pelo print
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT r, int size);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

    public static void TemaEscuro(IntPtr h)
    {
        try { SetWindowTheme(h, "DarkMode_Explorer", null); } catch { }
    }
}


// ---------------------------------------------------------------------
// 3. APARENCIA
// ---------------------------------------------------------------------
static class Tema
{
    public static readonly Color Borda    = Color.FromArgb(56, 56, 68);
    public static readonly Color Fundo    = Color.FromArgb(22, 22, 27);
    public static readonly Color Painel   = Color.FromArgb(30, 30, 37);
    public static readonly Color Campo    = Color.FromArgb(42, 42, 52);
    public static readonly Color CampoSob = Color.FromArgb(52, 52, 64);
    public static readonly Color Selecao  = Color.FromArgb(50, 44, 74);
    public static readonly Color Destaque = Color.FromArgb(139, 92, 246);
    public static readonly Color Texto    = Color.FromArgb(236, 236, 242);
    public static readonly Color Suave    = Color.FromArgb(146, 146, 162);
    public static readonly Color Fechar   = Color.FromArgb(196, 43, 28);
    public static readonly Color Perigo   = Color.FromArgb(240, 90, 80);
    public static readonly Color Sucesso  = Color.FromArgb(80, 200, 120);

    public static readonly Font Titulo   = new Font("Segoe UI Semibold", 12f);
    public static readonly Font Aba      = new Font("Segoe UI Semibold", 10.5f);
    public static readonly Font Normal   = new Font("Segoe UI", 10f);
    public static readonly Font Negrito  = new Font("Segoe UI Semibold", 10f);
    public static readonly Font Pequena  = new Font("Segoe UI", 8.5f);
    public static readonly Font Mono     = new Font("Consolas", 10.5f);
    public static readonly Font Icone    = new Font("Segoe UI Semibold", 11f);
    public static readonly Font Simbolo  = new Font("Marlett", 9f);
    public static readonly Font Engrenagem = new Font("Segoe UI Symbol", 13f);
    static Font emote;
    public static Font Emote { get { return emote ?? (emote = new Font("Segoe UI Emoji", 17f)); } }

    public const TextFormatFlags Linha = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
    public const TextFormatFlags Centro = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

    public const string Ponto = "  \u2022  ";

    // Pinceis reaproveitados
    static readonly Dictionary<int, SolidBrush> pinceis = new Dictionary<int, SolidBrush>();
    public static Brush Pincel(Color c)
    {
        SolidBrush b;
        if (!pinceis.TryGetValue(c.ToArgb(), out b)) { b = new SolidBrush(c); pinceis[c.ToArgb()] = b; }
        return b;
    }

    public static GraphicsPath Arredondado(Rectangle r, int raio)
    {
        int d = raio * 2;
        GraphicsPath p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static string Hora(DateTime h)
    {
        DateTime hoje = DateTime.Today;
        if (h.Date == hoje) return "Hoje, " + h.ToString("HH:mm");
        if (h.Date == hoje.AddDays(-1)) return "Ontem, " + h.ToString("HH:mm");
        return h.ToString("dd'/'MM', 'HH:mm");
    }

    // "Coracao" e "cora\u00e7\u00e3o" viram a mesma coisa na busca
    public static string Dobrar(string s)
    {
        string d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder(d.Length);
        foreach (char c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    // Titulo = primeira linha que nao esta vazia, com espacos juntados
    public static string TituloDe(string t)
    {
        string linha = "";
        using (StringReader r = new StringReader(t))
        {
            string l;
            while ((l = r.ReadLine()) != null)
                if (l.Trim().Length > 0) { linha = l; break; }
        }
        StringBuilder sb = new StringBuilder(Math.Min(linha.Length, 200));
        bool espaco = false;
        foreach (char c in linha.Trim())
        {
            if (char.IsWhiteSpace(c)) { espaco = true; continue; }
            if (espaco) { sb.Append(' '); espaco = false; }
            sb.Append(c);
            if (sb.Length >= 200) break;
        }
        return sb.ToString();
    }

    // Icone roxo de prancheta, desenhado na hora
    public static Icon CriarIcone()
    {
        using (Bitmap b = new Bitmap(32, 32))
        {
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath corpo = Arredondado(new Rectangle(4, 5, 24, 26), 5)) g.FillPath(Pincel(Destaque), corpo);
                using (GraphicsPath clipe = Arredondado(new Rectangle(10, 2, 12, 7), 2)) g.FillPath(Brushes.White, clipe);
                using (Pen p = new Pen(Color.White, 2.5f))
                {
                    g.DrawLine(p, 10, 15, 22, 15);
                    g.DrawLine(p, 10, 20, 22, 20);
                    g.DrawLine(p, 10, 25, 17, 25);
                }
            }
            IntPtr h = b.GetHicon();
            Icon temp = Icon.FromHandle(h);
            Icon copia = (Icon)temp.Clone();
            temp.Dispose();
            Nativo.DestroyIcon(h);
            return copia;
        }
    }
}

// ---------------------------------------------------------------------
// 4. UM ITEM DO HISTORICO
// ---------------------------------------------------------------------
class Item
{
    public string   Caminho;
    public DateTime Hora;
    public bool     Imagem;
    public string   Titulo = "";
    public string   Info   = "";
    public int      Tamanho;
    public int      Hash;
    Bitmap          mini;      // miniatura (so imagens), feita na primeira vez que aparece
    bool            miniFalhou;
    string texto;              // null quando o texto e grande e fica so no disco

    public Bitmap Mini
    {
        get
        {
            if (mini != null || miniFalhou || !Imagem) return mini;
            try
            {
                using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(Caminho)))
                using (Image img = Image.FromStream(ms, false, false))
                    mini = Miniatura(img);
            }
            catch { miniFalhou = true; }
            return mini;
        }
    }

    public void LiberarMini()
    {
        if (mini != null) { mini.Dispose(); mini = null; }
    }

    public string Texto
    {
        get
        {
            if (texto != null) return texto;
            try { return File.ReadAllText(Caminho, Encoding.UTF8); } catch { return ""; }
        }
    }

    public bool Contem(string filtro)
    {
        return Texto.IndexOf(filtro, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static Item DeArquivo(string caminho)
    {
        if (caminho.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            // Na abertura so le o cabecalho do PNG: nenhuma imagem e decodificada
            Item it = new Item();
            it.Caminho = caminho;
            it.Hora    = HoraDoNome(caminho);
            it.Imagem  = true;
            it.Titulo  = "Imagem";
            int w, h;
            if (TamanhoDoPng(caminho, out w, out h)) it.Info = w + " x " + h;
            return it;
        }
        return DeTexto(caminho, File.ReadAllText(caminho, Encoding.UTF8));
    }

    public static Item DeImagem(string caminho, Image img)
    {
        Item it = new Item();
        it.Caminho = caminho;
        it.Hora    = HoraDoNome(caminho);
        it.Imagem  = true;
        it.Titulo  = "Imagem";
        it.Info    = img.Width + " x " + img.Height;
        it.mini    = Miniatura(img);
        return it;
    }

    // Largura e altura ficam nos bytes 16 a 23 de todo PNG
    public static bool TamanhoDoPng(string caminho, out int w, out int h)
    {
        w = h = 0;
        try
        {
            byte[] b = new byte[24];
            using (FileStream fs = File.OpenRead(caminho))
                if (fs.Read(b, 0, 24) < 24) return false;
            if (b[0] != 0x89 || b[1] != 0x50 || b[2] != 0x4E || b[3] != 0x47) return false;
            w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return w > 0 && h > 0;
        }
        catch { return false; }
    }

    public static Item DeTexto(string caminho, string t)
    {
        Item it = new Item();
        it.Caminho = caminho;
        it.Hora    = HoraDoNome(caminho);
        it.Tamanho = t.Length;
        it.Hash    = t.GetHashCode();
        it.texto   = t.Length <= Config.TextoEmMemoria ? t : null;
        it.Info    = t.Length == 1 ? "1 caractere" : t.Length + " caracteres";

        it.Titulo  = Tema.TituloDe(t);
        return it;
    }

    public static DateTime HoraDoNome(string caminho)
    {
        DateTime d;
        string nome = Path.GetFileNameWithoutExtension(caminho);
        if (DateTime.TryParseExact(nome, "yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            return d;
        try { return File.GetLastWriteTime(caminho); } catch { return DateTime.Now; }
    }

    // Miniatura 56x44 recortada no centro
    static Bitmap Miniatura(Image img)
    {
        const int w = 56, h = 44;
        float esc = Math.Max((float)w / img.Width, (float)h / img.Height);
        int tw = (int)Math.Ceiling(img.Width * esc);
        int th = (int)Math.Ceiling(img.Height * esc);
        Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.DrawImage(img, (w - tw) / 2, (h - th) / 2, tw, th);
        }
        return b;
    }
}


// ---------------------------------------------------------------------
// 5. HISTORICO (lista em memoria + arquivos na pasta)
// ---------------------------------------------------------------------
static class Historico
{
    public static readonly List<Item> Itens = new List<Item>();   // mais novo primeiro

    public static void Carregar()
    {
        Directory.CreateDirectory(Config.Pasta);
        List<string> arquivos = new List<string>();
        foreach (string f in Directory.GetFiles(Config.Pasta))
            if (EhDoHistorico(f)) arquivos.Add(f);
        arquivos.Sort(delegate(string a, string b) { return string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)); });

        foreach (string f in arquivos)
        {
            try { Itens.Add(Item.DeArquivo(f)); }
            catch (Exception ex) { App.Log("Nao foi possivel ler " + f + ": " + ex.Message); }
        }
        Limitar();
        Expirar(DateTime.Now);
    }

    // Apaga o que passou do prazo escolhido e diz quantos itens saíram.
    // Vale para o historico; textos salvos e a pasta de prints nao entram.
    public static int Expirar(DateTime agora)
    {
        if (Config.PrazoHoras <= 0) return 0;
        DateTime limite = agora.AddHours(-Config.PrazoHoras);
        int apagados = 0;
        for (int i = Itens.Count - 1; i >= 0; i--)
        {
            if (Itens[i].Hora >= limite) continue;
            Remover(Itens[i]);
            apagados++;
        }
        return apagados;
    }

    // Quando o item mais antigo vence (null = nada para vencer)
    public static DateTime? ProximoVencimento()
    {
        if (Config.PrazoHoras <= 0 || Itens.Count == 0) return null;
        DateTime velho = DateTime.MaxValue;
        foreach (Item it in Itens) if (it.Hora < velho) velho = it.Hora;
        return velho.AddHours(Config.PrazoHoras);
    }

    // So e do historico o arquivo com o nome que o proprio programa da
    // (20261105_002426867.png). O "Limpar tudo" apagava a pasta inteira, e
    // foi assim que o codigo-fonte sumiu quando estava nesta pasta.
    public static bool EhDoHistorico(string caminho)
    {
        string ext = Path.GetExtension(caminho).ToLowerInvariant();
        if (ext != ".txt" && ext != ".png") return false;
        DateTime d;
        return DateTime.TryParseExact(Path.GetFileNameWithoutExtension(caminho), "yyyyMMdd_HHmmssfff",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
    }

    public static string NovoCaminho(string extensao)
    {
        DateTime agora = DateTime.Now;
        string p;
        do
        {
            p = Path.Combine(Config.Pasta, agora.ToString("yyyyMMdd_HHmmssfff") + extensao);
            agora = agora.AddMilliseconds(1);
        } while (File.Exists(p));
        return p;
    }

    public static void AdicionarTexto(string t)
    {
        if (string.IsNullOrWhiteSpace(t)) return;

        // Texto repetido: so sobe para o topo
        int h = t.GetHashCode();
        Item igual = null;
        foreach (Item it in Itens)
            if (!it.Imagem && it.Hash == h && it.Tamanho == t.Length && it.Texto == t) { igual = it; break; }
        if (igual != null) { ParaOTopo(igual); return; }

        Directory.CreateDirectory(Config.Pasta);
        string p = NovoCaminho(".txt");
        File.WriteAllText(p, t, new UTF8Encoding(true));
        Itens.Insert(0, Item.DeTexto(p, t));
        Limitar();
    }

    // Entra na lista depois que o arquivo ja foi gravado (ver App.GuardarImagem)
    public static void Inserir(Item it)
    {
        Itens.Insert(0, it);
        Limitar();
    }

    // Mantem no maximo X textos e Y imagens (apaga os mais antigos)
    public static void Limitar()
    {
        int textos = 0, imagens = 0;
        for (int i = 0; i < Itens.Count; i++)
        {
            Item it = Itens[i];
            bool sobra = it.Imagem ? ++imagens > Config.MaxImagens : ++textos > Config.MaxTextos;
            if (sobra) { Remover(it); i--; }
        }
    }

    public static void ParaOTopo(Item it)
    {
        string novo = NovoCaminho(Path.GetExtension(it.Caminho));
        try { File.Move(it.Caminho, novo); it.Caminho = novo; it.Hora = DateTime.Now; }
        catch { }
        Itens.Remove(it);
        Itens.Insert(0, it);
    }

    public static void Remover(Item it)
    {
        try { File.Delete(it.Caminho); } catch { }
        it.LiberarMini();
        Itens.Remove(it);
    }

    // Apaga a lista e os arquivos do historico. So os do historico: qualquer
    // outro arquivo que esteja na pasta fica onde esta.
    public static void LimparTudo()
    {
        foreach (Item it in Itens) it.LiberarMini();
        Itens.Clear();
        if (!Directory.Exists(Config.Pasta)) return;
        foreach (string f in Directory.GetFiles(Config.Pasta))
        {
            if (!EhDoHistorico(f)) continue;
            try { File.Delete(f); } catch { }
        }
    }

    // Le uma imagem sem deixar o arquivo travado
    public static Bitmap LerImagem(string caminho)
    {
        using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(caminho)))
        using (Image img = Image.FromStream(ms))
            return new Bitmap(img);
    }

    // Imagem so do tamanho da caixa de previa: um print 4K ocuparia 33 MB
    public static Bitmap LerPrevia(string caminho, int larguraMax, int alturaMax)
    {
        using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(caminho)))
        using (Image img = Image.FromStream(ms, false, false))
        {
            float esc = Math.Min(1f, Math.Min((float)larguraMax / img.Width, (float)alturaMax / img.Height));
            int w = Math.Max(1, (int)(img.Width * esc)), h = Math.Max(1, (int)(img.Height * esc));
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
                g.DrawImage(img, 0, 0, w, h);
            }
            return b;
        }
    }

    // Grava a imagem do historico onde a pessoa escolheu. PNG e copia do
    // arquivo (sem perda e na hora); JPG e BMP nao tem transparencia, entao
    // vao sobre fundo branco.
    public static void SalvarComo(Item it, string destino)
    {
        string ext = Path.GetExtension(destino).ToLowerInvariant();
        if (ext == ".png") { File.Copy(it.Caminho, destino, true); return; }
        using (Bitmap img = LerImagem(it.Caminho)) Gravar(img, destino);
    }

    // Copia da imagem sobre fundo branco, para quem nao entende transparencia
    public static Bitmap SemTransparencia(Image img)
    {
        Bitmap plano = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(plano))
        {
            g.Clear(Color.White);
            g.DrawImage(img, 0, 0, img.Width, img.Height);
        }
        return plano;
    }

    public static void Gravar(Image img, string destino)
    {
        string ext = Path.GetExtension(destino).ToLowerInvariant();
        if (ext == ".png") { img.Save(destino, ImageFormat.Png); return; }

        using (Bitmap plano = SemTransparencia(img))
        {
            if (ext == ".bmp") { plano.Save(destino, ImageFormat.Bmp); return; }

            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
            {
                if (c.FormatID != ImageFormat.Jpeg.Guid) continue;
                using (EncoderParameters p = new EncoderParameters(1))
                {
                    p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                    plano.Save(destino, c, p);
                }
                return;
            }
            plano.Save(destino, ImageFormat.Jpeg);
        }
    }
}

// ---------------------------------------------------------------------
// 5b. TEXTOS SALVOS (ficam em %APPDATA%, o "Limpar tudo" nao apaga)
// ---------------------------------------------------------------------
class Salvo
{
    public string   Caminho;
    public string   Texto;
    public string   Titulo;
    public DateTime Hora;
    public Item     Foto;      // imagem do salvo, ou null

    public bool TemImagem { get { return Foto != null; } }
    public bool TemTexto  { get { return Texto.Trim().Length > 0; } }
}

// O que vai ser entregue de um salvo que tem imagem. Nao e um Item do historico
// de proposito: la dentro a imagem entraria no prazo e seria apagada.
class EnvioSalvo
{
    public readonly Salvo Salvo;
    public readonly bool  ComTexto;
    public EnvioSalvo(Salvo salvo, bool comTexto) { Salvo = salvo; ComTexto = comTexto; }
}

static class Salvos
{
    static List<Salvo> itens;   // carregado so quando a aba e aberta

#if TESTE
    public static string PastaTeste;
    public static void Esquecer() { itens = null; }
    static string Pasta { get { return PastaTeste ?? Path.Combine(Config.PastaDados, "salvos"); } }
#else
    static string Pasta { get { return Path.Combine(Config.PastaDados, "salvos"); } }
#endif

    public static List<Salvo> Itens
    {
        get { if (itens == null) Carregar(); return itens; }
    }

    static void Carregar()
    {
        itens = new List<Salvo>();
        if (!Directory.Exists(Pasta)) return;
        string[] arquivos = Directory.GetFiles(Pasta, "*.txt");
        Array.Sort(arquivos, StringComparer.Ordinal);
        Array.Reverse(arquivos);   // mais novo primeiro
        foreach (string f in arquivos)
        {
            try { itens.Add(Criar(f, File.ReadAllText(f, Encoding.UTF8))); }
            catch (Exception ex) { App.Log("Nao foi possivel ler " + f + ": " + ex.Message); }
        }
    }

    static Salvo Criar(string caminho, string texto)
    {
        Salvo s = new Salvo();
        s.Caminho = caminho;
        s.Texto   = texto;
        s.Hora    = Item.HoraDoNome(caminho);
        string png = CaminhoImagem(s);
        if (File.Exists(png)) s.Foto = Item.DeArquivo(png);
        s.Titulo  = TituloDe(s);
        return s;
    }

    static string CaminhoImagem(Salvo s) { return Path.ChangeExtension(s.Caminho, ".png"); }

    static string TituloDe(Salvo s) { return s.TemTexto ? Tema.TituloDe(s.Texto) : "Imagem salva"; }

    public static Salvo Adicionar(string texto) { return Adicionar(texto, null); }

    // A imagem fica ao lado do texto, com o mesmo nome. Sem texto, salva so a imagem.
    public static Salvo Adicionar(string texto, Image imagem)
    {
        Directory.CreateDirectory(Pasta);
        DateTime agora = DateTime.Now;
        string p;
        do
        {
            p = Path.Combine(Pasta, agora.ToString("yyyyMMdd_HHmmssfff") + ".txt");
            agora = agora.AddMilliseconds(1);
        } while (File.Exists(p));

        if (imagem != null) Historico.Gravar(imagem, Path.ChangeExtension(p, ".png"));
        File.WriteAllText(p, texto, new UTF8Encoding(true));
        Salvo s = Criar(p, texto);
        Itens.Insert(0, s);
        return s;
    }

    // imagem = null tira a imagem do salvo
    public static void DefinirImagem(Salvo s, Image imagem)
    {
        string png = CaminhoImagem(s);
        if (s.Foto != null) { s.Foto.LiberarMini(); s.Foto = null; }
        if (imagem == null) File.Delete(png);
        else
        {
            Historico.Gravar(imagem, png);
            s.Foto = Item.DeArquivo(png);
        }
        s.Titulo = TituloDe(s);
    }

    public static void Alterar(Salvo s, string texto)
    {
        File.WriteAllText(s.Caminho, texto, new UTF8Encoding(true));
        s.Texto  = texto;
        s.Titulo = TituloDe(s);
    }

    public static void Remover(Salvo s)
    {
        try { File.Delete(s.Caminho); } catch { }
        try { File.Delete(CaminhoImagem(s)); } catch { }
        if (s.Foto != null) s.Foto.LiberarMini();
        Itens.Remove(s);
    }
}


// ---------------------------------------------------------------------
// 5c. EMOTES (lista embutida no .exe, carregada so quando a aba abre)
// ---------------------------------------------------------------------
class Emote
{
    public int    Categoria;
    public string Simbolo;
    public string Nome;
    public string Busca;   // nome + palavras-chave, sem acento
}

static class Emotes
{
    public const int Recentes = -1;

    public static readonly string[] Categorias =
    {
        "Carinhas", "Pessoas", "Animais e natureza", "Comidas", "Viagens e lugares",
        "Atividades", "Objetos", "S\u00edmbolos", "Bandeiras"
    };

    public static readonly string[] IconesCategorias =
    {
        "\U0001F600", "\U0001F44B", "\U0001F436", "\U0001F354", "\u2708",
        "\u26bd", "\U0001F4A1", "\u2764", "\U0001F3C1"
    };

    static List<Emote> todos;
    static List<string> recentes;

    public static List<Emote> Todos
    {
        get { if (todos == null) Carregar(); return todos; }
    }

    static void Carregar()
    {
        todos = new List<Emote>(1800);
        try
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("emojis.gz");
            if (s == null) return;
            using (s)
            using (GZipStream gz = new GZipStream(s, CompressionMode.Decompress))
            using (StreamReader r = new StreamReader(gz, Encoding.UTF8))
            {
                string l;
                while ((l = r.ReadLine()) != null)
                {
                    string[] p = l.Split('\t');
                    if (p.Length < 4 || p[0].Length == 0) continue;
                    Emote e = new Emote();
                    e.Categoria = p[0][0] - '0';
                    e.Simbolo   = p[1];
                    e.Nome      = p[2];
                    e.Busca     = p[3];
                    todos.Add(e);
                }
            }
        }
        catch (Exception ex) { App.Log("Erro ao carregar emotes: " + ex.Message); }
    }

    // Solta a lista da memoria quando a janela fecha
    public static void Liberar() { todos = null; }

    public static List<Emote> Filtrar(int categoria, string busca)
    {
        List<Emote> r = new List<Emote>();
        string q = Tema.Dobrar(busca.Trim());

        if (q.Length > 0)
        {
            string[] palavras = q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (Emote e in Todos)
            {
                bool ok = true;
                foreach (string w in palavras)
                    if (e.Busca.IndexOf(w, StringComparison.Ordinal) < 0) { ok = false; break; }
                if (ok) r.Add(e);
            }
            return r;
        }

        if (categoria == Recentes)
        {
            foreach (string sim in ListaRecentes)
            {
                Emote e = Achar(sim);
                if (e != null) r.Add(e);
            }
            return r;
        }

        foreach (Emote e in Todos) if (e.Categoria == categoria) r.Add(e);
        return r;
    }

    static Emote Achar(string simbolo)
    {
        foreach (Emote e in Todos) if (e.Simbolo == simbolo) return e;
        return null;
    }

    // ---- Recentes ----
    static string ArquivoRecentes { get { return Path.Combine(Config.PastaDados, "recentes.txt"); } }

    public static List<string> ListaRecentes
    {
        get
        {
            if (recentes == null)
            {
                recentes = new List<string>();
                try
                {
                    if (File.Exists(ArquivoRecentes))
                        foreach (string l in File.ReadAllLines(ArquivoRecentes, Encoding.UTF8))
                            if (l.Length > 0) recentes.Add(l);
                }
                catch { }
            }
            return recentes;
        }
    }

    public static void MarcarUsado(string simbolo)
    {
        List<string> r = ListaRecentes;
        r.Remove(simbolo);
        r.Insert(0, simbolo);
        while (r.Count > Config.MaxRecentes) r.RemoveAt(r.Count - 1);
        try
        {
            Directory.CreateDirectory(Config.PastaDados);
            File.WriteAllLines(ArquivoRecentes, r.ToArray(), new UTF8Encoding(false));
        }
        catch { }
    }
}


// ---------------------------------------------------------------------
// 5d. EMOJI COLORIDO
//
// GDI e GDI+ (o desenho normal do Windows Forms) so sabem fazer emoji
// preto e branco. Quem desenha a cor e o Direct2D com o DirectWrite, que
// usam a fonte colorida que ja vem no Windows. O .NET nao traz essas
// interfaces, entao as funcoes sao chamadas direto pela posicao de cada
// uma na tabela do objeto (os numeros abaixo sao da documentacao do SDK).
//
// Cada emoji vira um Bitmap transparente guardado em cache enquanto a
// janela esta aberta. Ao fechar, tudo e solto. Sem Direct2D, Disponivel
// fica false e a grade volta ao emoji preto e branco.
// ---------------------------------------------------------------------
static class EmojiCor
{
    [DllImport("d2d1.dll")] static extern int D2D1CreateFactory(int tipo, ref Guid iid, IntPtr opcoes, out IntPtr fabrica);
    [DllImport("dwrite.dll")] static extern int DWriteCreateFactory(int tipo, ref Guid iid, out IntPtr fabrica);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref InfoBitmap bi, uint uso, out IntPtr bits, IntPtr secao, uint desloc);

    [StructLayout(LayoutKind.Sequential)]
    struct InfoBitmap
    {
        public int Tamanho, Largura, Altura;
        public short Planos, Bits;
        public int Compressao, TamanhoImagem, Xppm, Yppm, Usadas, Importantes;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropsAlvo { public int Tipo, Formato, Alfa; public float DpiX, DpiY; public int Uso, Nivel; }
    [StructLayout(LayoutKind.Sequential)] struct CorF { public float R, G, B, A; }
    [StructLayout(LayoutKind.Sequential)] struct RetF { public float E, C, D, B; }

    delegate uint FnSoltar(IntPtr eu);
    delegate int  FnCriarAlvo(IntPtr eu, ref PropsAlvo p, out IntPtr alvo);
    delegate int  FnCriarFormato(IntPtr eu, [MarshalAs(UnmanagedType.LPWStr)] string familia, IntPtr colecao, int peso, int estilo,
                                 int largura, float tamanho, [MarshalAs(UnmanagedType.LPWStr)] string local, out IntPtr formato);
    delegate int  FnDefinir(IntPtr eu, int valor);
    delegate int  FnCriarPincel(IntPtr eu, ref CorF cor, IntPtr props, out IntPtr pincel);
    delegate void FnDesenhar(IntPtr eu, [MarshalAs(UnmanagedType.LPWStr)] string texto, int tamanho, IntPtr formato,
                             ref RetF ret, IntPtr pincel, int opcoes, int medida);
    delegate void FnLimpar(IntPtr eu, ref CorF cor);
    delegate void FnIniciar(IntPtr eu);
    delegate int  FnTerminar(IntPtr eu, IntPtr a, IntPtr b);
    delegate int  FnLigar(IntPtr eu, IntPtr hdc, ref Nativo.RECT r);
    delegate void FnModoTexto(IntPtr eu, int modo);

    const int MaxCache = 400;   // perto de 7 MB; passou disso, recomeca
    const int LadoGrade = 40;
    const float FonteGrade = 27f;

    static bool falhou;
    static IntPtr fabrica, escrita, alvo, pincel;
    static IntPtr dc, dib, bits;
    static int ladoDib;
    static FnDesenhar desenhar;
    static FnLimpar limpar;
    static FnIniciar iniciar;
    static FnTerminar terminar;
    static FnLigar ligar;
    static readonly Dictionary<int, IntPtr> formatos = new Dictionary<int, IntPtr>();
    static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

    static T Funcao<T>(IntPtr objeto, int posicao) where T : class
    {
        IntPtr tabela = Marshal.ReadIntPtr(objeto);
        IntPtr f = Marshal.ReadIntPtr(tabela, posicao * IntPtr.Size);
        return (T)(object)Marshal.GetDelegateForFunctionPointer(f, typeof(T));
    }

    static void Soltar(ref IntPtr objeto)
    {
        if (objeto == IntPtr.Zero) return;
        Funcao<FnSoltar>(objeto, 2)(objeto);
        objeto = IntPtr.Zero;
    }

    static bool Preparar()
    {
        if (alvo != IntPtr.Zero) return true;
        if (falhou) return false;
        try
        {
            Guid idD2d = new Guid("06152247-6f50-465a-9245-118bfd3b6007");
            Guid idDw  = new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
            if (D2D1CreateFactory(0, ref idD2d, IntPtr.Zero, out fabrica) < 0) throw new Exception("D2D1CreateFactory");
            if (DWriteCreateFactory(0, ref idDw, out escrita) < 0) throw new Exception("DWriteCreateFactory");

            // Software: nao abre a placa de video so para desenhar emoji
            PropsAlvo p = new PropsAlvo();
            p.Tipo = 1;      // D2D1_RENDER_TARGET_TYPE_SOFTWARE
            p.Formato = 87;  // DXGI_FORMAT_B8G8R8A8_UNORM
            p.Alfa = 1;      // D2D1_ALPHA_MODE_PREMULTIPLIED
            p.DpiX = p.DpiY = 96f;
            if (Funcao<FnCriarAlvo>(fabrica, 16)(fabrica, ref p, out alvo) < 0) throw new Exception("CreateDCRenderTarget");

            CorF preto = new CorF();
            preto.A = 1f;
            if (Funcao<FnCriarPincel>(alvo, 8)(alvo, ref preto, IntPtr.Zero, out pincel) < 0) throw new Exception("CreateSolidColorBrush");

            Funcao<FnModoTexto>(alvo, 34)(alvo, 2);   // suavizacao em tons de cinza: combina com fundo transparente
            desenhar = Funcao<FnDesenhar>(alvo, 27);
            limpar   = Funcao<FnLimpar>(alvo, 47);
            iniciar  = Funcao<FnIniciar>(alvo, 48);
            terminar = Funcao<FnTerminar>(alvo, 49);
            ligar    = Funcao<FnLigar>(alvo, 57);
            return true;
        }
        catch (Exception ex)
        {
            App.Log("Emoji colorido indisponivel: " + ex.Message);
            falhou = true;
            Liberar();
            return false;
        }
    }

    static IntPtr Formato(float tamanho)
    {
        int chave = (int)(tamanho * 10);
        IntPtr f;
        if (formatos.TryGetValue(chave, out f)) return f;
        if (Funcao<FnCriarFormato>(escrita, 15)(escrita, "Segoe UI Emoji", IntPtr.Zero, 400, 0, 5, tamanho, "pt-br", out f) < 0)
            return IntPtr.Zero;
        Funcao<FnDefinir>(f, 3)(f, 2);   // centro na horizontal
        Funcao<FnDefinir>(f, 4)(f, 2);   // centro na vertical
        Funcao<FnDefinir>(f, 5)(f, 1);   // sem quebra de linha
        formatos[chave] = f;
        return f;
    }

    static bool PrepararDib(int lado)
    {
        if (dib != IntPtr.Zero && ladoDib == lado) return true;
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        if (dc == IntPtr.Zero) dc = CreateCompatibleDC(IntPtr.Zero);
        InfoBitmap bi = new InfoBitmap();
        bi.Tamanho = Marshal.SizeOf(typeof(InfoBitmap));
        bi.Largura = lado;
        bi.Altura  = -lado;   // de cima para baixo, igual ao Bitmap
        bi.Planos  = 1;
        bi.Bits    = 32;
        dib = CreateDIBSection(dc, ref bi, 0, out bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero) return false;
        SelectObject(dc, dib);
        ladoDib = lado;
        return true;
    }

    // Desenha um emoji num Bitmap novo (quem chamou cuida dele). null = nao deu.
    public static Bitmap Criar(string simbolo, int lado, float tamanhoFonte)
    {
        if (string.IsNullOrEmpty(simbolo) || !Preparar()) return null;
        try
        {
            IntPtr formato = Formato(tamanhoFonte);
            if (formato == IntPtr.Zero || !PrepararDib(lado)) return null;

            Nativo.RECT r = new Nativo.RECT();
            r.Right = lado; r.Bottom = lado;
            if (ligar(alvo, dc, ref r) < 0) return null;

            RetF area = new RetF();
            area.D = lado; area.B = lado;
            CorF vazio = new CorF();
            iniciar(alvo);
            limpar(alvo, ref vazio);
            desenhar(alvo, simbolo, simbolo.Length, formato, ref area, pincel, 4, 0);   // 4 = fonte colorida
            if (terminar(alvo, IntPtr.Zero, IntPtr.Zero) < 0) return null;
            GdiFlush();

            Bitmap b = new Bitmap(lado, lado, PixelFormat.Format32bppPArgb);
            BitmapData d = b.LockBits(new Rectangle(0, 0, lado, lado), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            Nativo.CopiarMemoria(d.Scan0, bits, new IntPtr(lado * lado * 4));
            b.UnlockBits(d);
            return b;
        }
        catch (Exception ex)
        {
            App.Log("Erro ao desenhar emoji: " + ex.Message);
            falhou = true;
            return null;
        }
    }

    // Emoji da grade, em cache. Quem recebe nao pode liberar o Bitmap.
    public static Bitmap DaGrade(string simbolo)
    {
        Bitmap b;
        if (cache.TryGetValue(simbolo, out b)) return b;
        if (cache.Count >= MaxCache) LimparCache();
        b = Criar(simbolo, LadoGrade, FonteGrade);
        if (b != null) cache[simbolo] = b;
        return b;
    }

    static void LimparCache()
    {
        foreach (Bitmap b in cache.Values) b.Dispose();
        cache.Clear();
    }

    // Solta tudo quando a janela fecha
    public static void Liberar()
    {
        LimparCache();
        foreach (IntPtr f in new List<IntPtr>(formatos.Values)) { IntPtr x = f; Soltar(ref x); }
        formatos.Clear();
        Soltar(ref pincel);
        Soltar(ref alvo);
        Soltar(ref escrita);
        Soltar(ref fabrica);
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        if (dc != IntPtr.Zero) { DeleteDC(dc); dc = IntPtr.Zero; }
        bits = IntPtr.Zero;
        ladoDib = 0;
        desenhar = null; limpar = null; iniciar = null; terminar = null; ligar = null;
    }
}


// ---------------------------------------------------------------------
// 6. NUCLEO: escuta a area de transferencia e o teclado
// ---------------------------------------------------------------------
class Nucleo : NativeWindow, IDisposable
{
    const int WM_HOTKEY          = 0x0312;
    const int WM_CLIPBOARDUPDATE = 0x031D;
    const int WM_ACAO            = 0x8001;   // mensagens proprias
    const int WM_GRAVOU          = 0x8002;

    public event EventHandler CopiaDetectada;
    public event Action<int> AcaoPedida;          // indice em Config.Atalhos
    public event Action<Atalho> TeclaGravada;     // usado na tela de configurar atalho
    public bool GanchoOk;
    public volatile bool Gravando;                // true = a proxima combinacao vai para TeclaGravada

    IntPtr gancho = IntPtr.Zero;
    IntPtr alvoMensagens;   // janela deste objeto, lida pela thread do gancho
    Nativo.HookProc proc;   // guardado para o coletor de lixo nao apagar
    int teclaEngolida;      // tecla escondida do Windows ate ser solta
    Thread threadGancho;
    uint idThreadGancho;

    public Nucleo()
    {
        CreateParams cp = new CreateParams();
        cp.Parent = new IntPtr(-3);   // janela invisivel, so recebe mensagens
        CreateHandle(cp);

        // Avisado pelo Windows a cada copia (sem ficar verificando)
        Nativo.AddClipboardFormatListener(Handle);

        RegistrarReservas();

        // Gancho de teclado: pega os atalhos antes do Windows e dos jogos.
        // Roda numa thread so dele porque TODA tecla do computador passa por
        // aqui: se o gancho dividisse a thread com a janela, gravar um PNG ou
        // abrir um dialogo deixaria o teclado inteiro travando.
        alvoMensagens = Handle;
#if !TESTE
        using (ManualResetEvent pronto = new ManualResetEvent(false))
        {
            threadGancho = new Thread(delegate() { LacoDoGancho(pronto); }, 128 * 1024);
            threadGancho.IsBackground = true;
            threadGancho.Priority = ThreadPriority.AboveNormal;
            threadGancho.Start();
            pronto.WaitOne(3000);
        }
#endif
    }

    void LacoDoGancho(ManualResetEvent pronto)
    {
        proc = Gancho;
        gancho = Nativo.SetWindowsHookEx(13, proc, Nativo.GetModuleHandle(null), 0);
        GanchoOk = gancho != IntPtr.Zero;
        idThreadGancho = Nativo.GetCurrentThreadId();
        pronto.Set();

        Nativo.MSG m;
        while (Nativo.GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }

        if (gancho != IntPtr.Zero) { Nativo.UnhookWindowsHookEx(gancho); gancho = IntPtr.Zero; }
    }

    // Reserva: registra os atalhos tambem do jeito normal do Windows
    public void RegistrarReservas()
    {
        for (int i = 0; i < Config.Atalhos.Length; i++)
        {
            Nativo.UnregisterHotKey(Handle, i + 1);
            Atalho a = Config.Atalhos[i];
            if (!a.Vazio) Nativo.RegisterHotKey(Handle, i + 1, (uint)(a.Mods | 0x4000), (uint)a.Vk);
        }
    }

    static bool Apertada(int vk) { return (Nativo.GetAsyncKeyState(vk) & 0x8000) != 0; }

    static bool EhModificador(int vk)
    {
        return vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
    }

    static int ModsAtuais()
    {
        int m = 0;
        if (Apertada(0x12)) m |= Atalho.Alt;
        if (Apertada(0x11)) m |= Atalho.Ctrl;
        if (Apertada(0x10)) m |= Atalho.Shift;
        if (Apertada(0x5B) || Apertada(0x5C)) m |= Atalho.Win;
        return m;
    }

    // Chamado a cada tecla. Fica o mais curto possivel.
    IntPtr Gancho(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk  = Marshal.ReadInt32(lParam);
            int msg = wParam.ToInt32();
            bool desce = msg == 0x0100 || msg == 0x0104;

            // 0xE8 e a tecla "vazia" que o proprio programa envia: nunca conta como atalho
            if (vk == 0xE8) return Nativo.CallNextHookEx(gancho, nCode, wParam, lParam);

            // Tecla de um atalho que ja disparou: esconde repeticao e o "soltar"
            if (vk == teclaEngolida)
            {
                if (!desce) teclaEngolida = 0;
                return new IntPtr(1);
            }

            if (desce && !EhModificador(vk))
            {
                if (Gravando)
                {
                    int mods = ModsAtuais();
                    Nativo.PostMessage(alvoMensagens, WM_GRAVOU, new IntPtr(vk), new IntPtr(mods));
                    Engolir(vk, mods);
                    return new IntPtr(1);
                }

                Atalho[] lista = Config.Atalhos;
                int modsAgora = -1;
                for (int i = 0; i < lista.Length; i++)
                {
                    if (lista[i].Vk != vk) continue;
                    if (modsAgora < 0) modsAgora = ModsAtuais();
                    if (lista[i].Mods != modsAgora) continue;

                    Nativo.PostMessage(alvoMensagens, WM_ACAO, new IntPtr(i), IntPtr.Zero);
                    Engolir(vk, modsAgora);
                    return new IntPtr(1);
                }
            }
        }
        return Nativo.CallNextHookEx(gancho, nCode, wParam, lParam);
    }

    void Engolir(int vk, int mods)
    {
        teclaEngolida = vk;
        // Tecla "vazia": evita que soltar o Win abra o Iniciar
        // ou que soltar o Alt ative o menu do programa
        if ((mods & (Atalho.Win | Atalho.Alt)) != 0)
        {
            Nativo.keybd_event(0xE8, 0, 0, UIntPtr.Zero);
            Nativo.keybd_event(0xE8, 0, 2, UIntPtr.Zero);
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_CLIPBOARDUPDATE:
                if (CopiaDetectada != null) CopiaDetectada(this, EventArgs.Empty);
                break;
            case WM_HOTKEY:
                int id = m.WParam.ToInt32() - 1;
                if (!Gravando && AcaoPedida != null && id >= 0 && id < Config.Atalhos.Length) AcaoPedida(id);
                break;
            case WM_ACAO:
                if (AcaoPedida != null) AcaoPedida(m.WParam.ToInt32());
                break;
            case WM_GRAVOU:
                if (TeclaGravada != null) TeclaGravada(new Atalho(m.LParam.ToInt32(), m.WParam.ToInt32()));
                break;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (idThreadGancho != 0)
        {
            Nativo.PostThreadMessage(idThreadGancho, 0x0012, IntPtr.Zero, IntPtr.Zero);   // WM_QUIT
            threadGancho.Join(500);
            idThreadGancho = 0;
        }
        Nativo.RemoveClipboardFormatListener(Handle);
        for (int i = 0; i < Config.Atalhos.Length; i++) Nativo.UnregisterHotKey(Handle, i + 1);
        DestroyHandle();
    }
}


// ---------------------------------------------------------------------
// 6a. SOM DO PRINT
//
// O som de sistema do Windows depende do esquema de sons e do canal "Sons
// do sistema", que pode estar mudo ou baixo. Este e um clique de camera
// gerado aqui mesmo: toca no volume normal e nao precisa de arquivo.
// ---------------------------------------------------------------------
static class Som
{
    const uint Assincrono = 1, SemPadrao = 2, DaMemoria = 4;
    const int Amostras = 22050;
    static IntPtr wav;   // fica na memoria nao gerenciada: o toque continua depois da chamada

    public static byte[] GerarWav()
    {
        int total = Amostras * 16 / 100;   // 160 ms
        short[] sons = new short[total];
        Random ruido = new Random(7);
        for (int i = 0; i < total; i++)
        {
            double t = (double)i / Amostras;
            // Dois estalos: a cortina abrindo e fechando
            double a = t < 0.05 ? Math.Exp(-t * 180) : 0;
            double b = t >= 0.07 ? Math.Exp(-(t - 0.07) * 120) : 0;
            double v = (a + b * 0.8) * (0.65 * (ruido.NextDouble() * 2 - 1) + 0.35 * Math.Sin(2 * Math.PI * 1500 * t));
            sons[i] = (short)(Math.Max(-1, Math.Min(1, v)) * 26000);
        }

        using (MemoryStream ms = new MemoryStream(44 + total * 2))
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + total * 2);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);          // PCM
            w.Write((short)1);          // mono
            w.Write(Amostras);
            w.Write(Amostras * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(total * 2);
            foreach (short a in sons) w.Write(a);
            w.Flush();
            return ms.ToArray();
        }
    }

    public static bool Tocar(uint extra = Assincrono)
    {
        try
        {
            if (wav == IntPtr.Zero)
            {
                byte[] b = GerarWav();
                wav = Marshal.AllocHGlobal(b.Length);
                Marshal.Copy(b, 0, wav, b.Length);
            }
            return Nativo.PlaySound(wav, IntPtr.Zero, extra | SemPadrao | DaMemoria);
        }
        catch (Exception ex)
        {
            App.Log("Erro ao tocar o som: " + ex.Message);
            return false;
        }
    }
}


// ---------------------------------------------------------------------
// 6b. PRINT (janela ativa ou tela inteira)
// ---------------------------------------------------------------------

static class Captura
{
    // Tira o print e devolve a imagem (quem chamou libera). A gravacao fica
    // para App.TirarPrint, fora da thread da janela.
    public static Bitmap Tirar(bool telaInteira)
    {
        // Le as coordenadas reais mesmo com zoom de tela (125%, 150%...)
        IntPtr dpiAntigo = IntPtr.Zero;
        try { dpiAntigo = Nativo.SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }

        try
        {
            IntPtr h = Nativo.GetForegroundWindow();
            Rectangle area = Rectangle.Empty;
            bool janela = !telaInteira && JanelaValida(h, out area);
            if (!janela) area = MonitorDe(h);

            Bitmap bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
                g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);

            // Alguns jogos aparecem pretos na copia de tela: tenta pela janela
            if (janela && QuasePreta(bmp)) TentarPrintWindow(h, area, bmp);
            return bmp;
        }
        finally
        {
            if (dpiAntigo != IntPtr.Zero)
                try { Nativo.SetThreadDpiAwarenessContext(dpiAntigo); } catch { }
        }
    }

    // Foto de todos os monitores, para o recorte. Devolve tambem onde ela fica,
    // em pixels reais. Se a janela da frente sair preta (alguns jogos), tenta
    // pela propria janela e coloca o resultado na foto.
    public static Bitmap TirarTudo(out Rectangle area)
    {
        IntPtr dpiAntigo = IntPtr.Zero;
        try { dpiAntigo = Nativo.SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }

        try
        {
            area = SystemInformation.VirtualScreen;
            Bitmap bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
                g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);

            IntPtr h = Nativo.GetForegroundWindow();
            Rectangle jan;
            if (JanelaValida(h, out jan))
            {
                Rectangle dentro = new Rectangle(jan.X - area.X, jan.Y - area.Y, jan.Width, jan.Height);
                using (Bitmap parte = new Bitmap(jan.Width, jan.Height, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(parte))
                        g.DrawImage(bmp, new Rectangle(0, 0, jan.Width, jan.Height), dentro, GraphicsUnit.Pixel);
                    if (QuasePreta(parte))
                    {
                        TentarPrintWindow(h, jan, parte);
                        using (Graphics g = Graphics.FromImage(bmp)) g.DrawImage(parte, dentro);
                    }
                }
            }
            return bmp;
        }
        finally
        {
            if (dpiAntigo != IntPtr.Zero)
                try { Nativo.SetThreadDpiAwarenessContext(dpiAntigo); } catch { }
        }
    }

    public static string CaminhoNovo()
    {
        Directory.CreateDirectory(Config.PastaPrints);
        string base_ = "captura-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string arq = Path.Combine(Config.PastaPrints, base_ + ".png");
        for (int n = 2; File.Exists(arq); n++)
            arq = Path.Combine(Config.PastaPrints, base_ + "_" + n + ".png");
        return arq;
    }

    static bool JanelaValida(IntPtr h, out Rectangle area)
    {
        area = Rectangle.Empty;
        if (h == IntPtr.Zero) return false;

        StringBuilder sb = new StringBuilder(64);
        Nativo.GetClassName(h, sb, sb.Capacity);
        string c = sb.ToString();
        if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;

        // Tamanho visivel (sem a sombra invisivel do Windows 10)
        Nativo.RECT r;
        if (Nativo.DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Nativo.RECT))) != 0)
            if (!Nativo.GetWindowRect(h, out r)) return false;

        area = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        area.Intersect(SystemInformation.VirtualScreen);
        return area.Width > 1 && area.Height > 1;
    }

    static Rectangle MonitorDe(IntPtr h)
    {
        Screen s = h != IntPtr.Zero ? Screen.FromHandle(h) : Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        return s.Bounds;
    }

    // Olha 400 pontos espalhados; se todos forem quase pretos, a copia falhou
    static bool QuasePreta(Bitmap b)
    {
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 20; x++)
            {
                Color c = b.GetPixel(b.Width * x / 20 + b.Width / 40, b.Height * y / 20 + b.Height / 40);
                if (c.R > 12 || c.G > 12 || c.B > 12) return false;
            }
        return true;
    }

    static void TentarPrintWindow(IntPtr h, Rectangle area, Bitmap destino)
    {
        Nativo.RECT w;
        if (!Nativo.GetWindowRect(h, out w)) return;
        int ww = w.Right - w.Left, wh = w.Bottom - w.Top;
        if (ww < 2 || wh < 2) return;

        using (Bitmap tudo = new Bitmap(ww, wh, PixelFormat.Format32bppRgb))
        {
            bool ok;
            using (Graphics g = Graphics.FromImage(tudo))
            {
                IntPtr hdc = g.GetHdc();
                ok = Nativo.PrintWindow(h, hdc, 2);   // 2 = inclui conteudo DirectX
                g.ReleaseHdc(hdc);
            }
            if (!ok || QuasePreta(tudo)) return;

            using (Graphics g = Graphics.FromImage(destino))
                g.DrawImage(tudo, new Rectangle(0, 0, area.Width, area.Height),
                    new Rectangle(area.Left - w.Left, area.Top - w.Top, area.Width, area.Height), GraphicsUnit.Pixel);
        }
    }
}

// Recorte de tela. A foto e tirada antes de esta janela existir: mesmo que um
// jogo em tela cheia perca o foco e minimize, o que se recorta e o quadro de
// quando o atalho foi apertado.
class TelaRecorte : Form
{
    readonly Bitmap foto, escura;
    readonly Point origemDica;
    bool selecionando;
    Point inicio;
    Rectangle selecao;

    public Bitmap Resultado;   // quem chamou libera; null = cancelou

    public TelaRecorte(Bitmap fotoDaTela, Rectangle area)
    {
        foto = fotoDaTela;
        escura = new Bitmap(foto.Width, foto.Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(escura))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(foto, new Rectangle(0, 0, foto.Width, foto.Height));
            g.CompositingMode = CompositingMode.SourceOver;
            using (Brush b = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.FillRectangle(b, 0, 0, foto.Width, foto.Height);
        }

        Text = "Recortar a tela";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        ShowInTaskbar   = false;
        TopMost         = true;
        KeyPreview      = true;
        BackColor       = Color.Black;
        Cursor          = Cursors.Cross;
        Bounds          = area;

        // Canto do monitor principal, que pode nao ser o canto da area toda
        origemDica = new Point(area.X <= 0 && area.Right > 0 ? 24 - area.X : 24,
                               area.Y <= 0 && area.Bottom > 0 ? 24 - area.Y : 24);

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
    }

    // A janela nasce em pixels reais, como a foto. Sem isso, com zoom de tela
    // (125%, 150%...) o Windows esticaria a foto e o recorte sairia deslocado.
    public void Mostrar()
    {
        IntPtr dpiAntigo = IntPtr.Zero;
        try { dpiAntigo = Nativo.SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }
        try { Show(); }
        finally
        {
            if (dpiAntigo != IntPtr.Zero)
                try { Nativo.SetThreadDpiAwarenessContext(dpiAntigo); } catch { }
        }
        Activate();
        Nativo.SetForegroundWindow(Handle);
    }

    Rectangle CaixaDoTamanho(Graphics g)
    {
        string t = Legenda;
        Size s = TextRenderer.MeasureText(t, Tema.Pequena);
        int x = selecao.Left;
        int y = selecao.Bottom + 6;
        if (y + s.Height + 8 > foto.Height) y = selecao.Bottom - s.Height - 14;
        if (x + s.Width + 12 > foto.Width) x = foto.Width - s.Width - 12;
        return new Rectangle(Math.Max(0, x), Math.Max(0, y), s.Width + 12, s.Height + 8);
    }

    string Legenda { get { return selecao.Width + " x " + selecao.Height; } }

    Rectangle AreaDePintura()
    {
        Rectangle r = selecao;
        using (Graphics g = CreateGraphics()) r = Rectangle.Union(r, CaixaDoTamanho(g));
        r.Inflate(3, 3);
        return r;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Rectangle c = e.ClipRectangle;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(escura, c, c, GraphicsUnit.Pixel);

        if (selecao.Width > 0 && selecao.Height > 0)
        {
            g.DrawImage(foto, selecao, selecao, GraphicsUnit.Pixel);
            g.CompositingMode = CompositingMode.SourceOver;
            using (Pen p = new Pen(Tema.Destaque, 2))
                g.DrawRectangle(p, selecao.X - 1, selecao.Y - 1, selecao.Width + 1, selecao.Height + 1);

            Rectangle r = CaixaDoTamanho(g);
            g.FillRectangle(Tema.Pincel(Color.FromArgb(235, 20, 20, 26)), r);
            TextRenderer.DrawText(g, Legenda, Tema.Pequena, r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            g.CompositingMode = CompositingMode.SourceOver;
            const string dica = "Arraste para recortar  •  Esc ou botão direito cancela";
            Size s = TextRenderer.MeasureText(dica, Tema.Normal);
            Rectangle r = new Rectangle(origemDica.X, origemDica.Y, s.Width + 24, s.Height + 14);
            g.FillRectangle(Tema.Pincel(Color.FromArgb(235, 20, 20, 26)), r);
            TextRenderer.DrawText(g, dica, Tema.Normal, r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right) { Close(); return; }
        if (e.Button != MouseButtons.Left) return;
        selecionando = true;
        inicio = e.Location;
        selecao = Rectangle.Empty;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!selecionando) return;
        Rectangle velha = AreaDePintura();
        int x = Math.Max(0, Math.Min(foto.Width, e.X)), y = Math.Max(0, Math.Min(foto.Height, e.Y));
        selecao = Rectangle.FromLTRB(Math.Min(inicio.X, x), Math.Min(inicio.Y, y), Math.Max(inicio.X, x), Math.Max(inicio.Y, y));
        Invalidate(Rectangle.Union(velha, AreaDePintura()));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!selecionando || e.Button != MouseButtons.Left) return;
        selecionando = false;

        // Clique sem arrastar nao recorta: continua esperando
        if (selecao.Width < 3 || selecao.Height < 3)
        {
            selecao = Rectangle.Empty;
            Invalidate();
            return;
        }

        Bitmap b = new Bitmap(selecao.Width, selecao.Height, PixelFormat.Format32bppRgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(foto, new Rectangle(0, 0, b.Width, b.Height), selecao, GraphicsUnit.Pixel);
        }
        Resultado = b;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys k)
    {
        if (k == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, k);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foto.Dispose();
            escura.Dispose();
        }
        base.Dispose(disposing);
    }
}

// ---------------------------------------------------------------------
// 7. CONTROLES VISUAIS
// ---------------------------------------------------------------------

// Cores do menu de botao direito (tema escuro)
class CoresMenu : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return Tema.Painel; } }
    public override Color ImageMarginGradientBegin    { get { return Tema.Painel; } }
    public override Color ImageMarginGradientMiddle   { get { return Tema.Painel; } }
    public override Color ImageMarginGradientEnd      { get { return Tema.Painel; } }
    public override Color MenuItemSelected            { get { return Tema.Selecao; } }
    public override Color MenuItemBorder              { get { return Tema.Selecao; } }
    public override Color MenuBorder                  { get { return Tema.Borda; } }
    public override Color SeparatorDark               { get { return Tema.Borda; } }
    public override Color SeparatorLight              { get { return Tema.Painel; } }
    public override Color CheckBackground             { get { return Tema.Selecao; } }
    public override Color CheckSelectedBackground     { get { return Tema.Selecao; } }
    public override Color CheckPressedBackground      { get { return Tema.Selecao; } }

    public static ContextMenuStrip NovoMenu()
    {
        ContextMenuStrip m = new ContextMenuStrip();
        m.Renderer  = new ToolStripProfessionalRenderer(new CoresMenu());
        m.BackColor = Tema.Painel;
        m.ForeColor = Tema.Texto;
        m.Font      = Tema.Normal;
        return m;
    }
}

// Dados de um cartao da lista
class Cartao
{
    public string Titulo, Info, Icone;
    public Font   FonteIcone;
    public Bitmap Mini;
}

// Lista de cartoes (usada no Historico e nos Salvos)
class ListaCartoes : ListBox
{
    public Action<int, Cartao> Preencher;
    readonly Cartao cartao = new Cartao();

    public ListaCartoes()
    {
        DrawMode       = DrawMode.OwnerDrawFixed;
        ItemHeight     = 60;
        IntegralHeight = false;
        BorderStyle    = BorderStyle.None;
        Dock           = DockStyle.Fill;
        BackColor      = Tema.Painel;
        ForeColor      = Tema.Texto;
    }

    public void Definir(int total)
    {
        BeginUpdate();
        Items.Clear();
        object[] v = new object[total];
        for (int i = 0; i < total; i++) v[i] = "";
        Items.AddRange(v);
        EndUpdate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Nativo.TemaEscuro(Handle);
    }

    // Botao direito tambem seleciona o item
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            int i = IndexFromPoint(e.Location);
            if (i >= 0) SelectedIndex = i;
        }
        base.OnMouseDown(e);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Preencher == null) return;
        Cartao c = cartao;
        c.Titulo = ""; c.Info = ""; c.Icone = null; c.FonteIcone = null; c.Mini = null;
        Preencher(e.Index, c);

        Graphics g = e.Graphics;
        Rectangle r = e.Bounds;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool selecionado = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        g.FillRectangle(Tema.Pincel(selecionado ? Tema.Selecao : Tema.Painel), r);
        if (selecionado) g.FillRectangle(Tema.Pincel(Tema.Destaque), r.X, r.Y + 12, 3, r.Height - 24);

        // Miniatura ou icone
        Rectangle caixa = new Rectangle(r.X + 14, r.Y + 8, 56, 44);
        using (GraphicsPath forma = Tema.Arredondado(caixa, 6))
        {
            g.FillPath(Tema.Pincel(Tema.Campo), forma);
            if (c.Mini != null)
            {
                g.SetClip(forma);
                g.DrawImage(c.Mini, caixa);
                g.ResetClip();
            }
            else if (c.Icone != null)
            {
                TextRenderer.DrawText(g, c.Icone, c.FonteIcone ?? Tema.Icone, caixa, Tema.Destaque, Tema.Centro);
            }
        }

        int x = caixa.Right + 12;
        int largura = r.Right - x - 12;
        TextRenderer.DrawText(g, c.Titulo, Tema.Normal, new Rectangle(x, r.Y + 9, largura, 22), Tema.Texto, Tema.Linha);
        TextRenderer.DrawText(g, c.Info, Tema.Pequena, new Rectangle(x, r.Y + 31, largura, 18), Tema.Suave, Tema.Linha);
        g.FillRectangle(Tema.Pincel(Tema.Fundo), r.X + 14, r.Bottom - 1, r.Width - 28, 1);
    }
}

// Grade de emotes: desenha so o que esta visivel
class GradeEmotes : Control
{
    public const int Celula = 46;
    const int Margem = 8;

    List<Emote> itens = new List<Emote>();
    int selecionado = -1, sobre = -1, rolagem;
    readonly VScrollBar barra;

    public event Action<Emote> Escolhido;
    public event Action<Emote> Destacado;

    public GradeEmotes()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Tema.Painel;
        barra = new VScrollBar();
        barra.Dock = DockStyle.Right;
        barra.Scroll += delegate { rolagem = barra.Value; Invalidate(); };
        Controls.Add(barra);
    }

    public Emote Atual
    {
        get { return selecionado >= 0 && selecionado < itens.Count ? itens[selecionado] : null; }
    }

    int Colunas { get { return Math.Max(1, (ClientSize.Width - barra.Width - Margem * 2) / Celula); } }

    int RolagemMax
    {
        get
        {
            int c = Colunas;
            int total = ((itens.Count + c - 1) / c) * Celula + Margem * 2;
            return Math.Max(0, total - ClientSize.Height);
        }
    }

    public void Definir(List<Emote> lista)
    {
        itens = lista;
        selecionado = lista.Count > 0 ? 0 : -1;
        sobre = -1;
        rolagem = 0;
        AjustarBarra();
        Invalidate();
        if (Destacado != null) Destacado(Atual);
    }

    void AjustarBarra()
    {
        int max = RolagemMax;
        rolagem = Math.Max(0, Math.Min(rolagem, max));
        int pagina = Math.Max(1, ClientSize.Height);
        barra.Minimum = 0;
        barra.Maximum = max + pagina - 1;   // primeiro o maximo, depois o tamanho da pagina
        barra.LargeChange = pagina;
        barra.SmallChange = Celula;
        barra.Value = rolagem;
        barra.Visible = max > 0;
    }

    void Rolar(int y)
    {
        rolagem = Math.Max(0, Math.Min(RolagemMax, y));
        barra.Value = rolagem;
        Invalidate();
    }

    Rectangle Ret(int i)
    {
        int c = Colunas;
        return new Rectangle(Margem + (i % c) * Celula, Margem + (i / c) * Celula - rolagem, Celula, Celula);
    }

    int Indice(Point p)
    {
        int x = p.X - Margem, y = p.Y + rolagem - Margem;
        if (x < 0 || y < 0) return -1;
        int col = x / Celula, lin = y / Celula;
        if (col >= Colunas) return -1;
        int i = lin * Colunas + col;
        return i < itens.Count ? i : -1;
    }

    public void Mover(int dx, int dy)
    {
        if (itens.Count == 0) return;
        int i = selecionado < 0 ? 0 : selecionado + dx + dy * Colunas;
        i = Math.Max(0, Math.Min(itens.Count - 1, i));
        selecionado = i;

        Rectangle r = Ret(i);
        if (r.Top < Margem) Rolar(rolagem + r.Top - Margem);
        else if (r.Bottom > ClientSize.Height - Margem) Rolar(rolagem + r.Bottom - ClientSize.Height + Margem);
        Invalidate();
        if (Destacado != null) Destacado(Atual);
    }

    public void EscolherAtual()
    {
        if (Atual != null && Escolhido != null) Escolhido(Atual);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Nativo.TemaEscuro(barra.Handle);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); AjustarBarra(); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Rolar(rolagem - e.Delta / 120 * Celula * 2);
        base.OnMouseWheel(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = Indice(e.Location);
        if (i != sobre)
        {
            // Redesenha so as duas celulas que mudaram
            if (sobre >= 0) Invalidate(Ret(sobre));
            if (i >= 0) Invalidate(Ret(i));
            sobre = i;
            if (Destacado != null) Destacado(i >= 0 ? itens[i] : Atual);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        sobre = -1;
        Invalidate();
        if (Destacado != null) Destacado(Atual);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int i = Indice(e.Location);
        if (i < 0) return;
        selecionado = i;
        Invalidate();
        if (Escolhido != null) Escolhido(itens[i]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;   // 1 para 1: sem custo de filtro
        g.PixelOffsetMode = PixelOffsetMode.Half;
        int c = Colunas;
        int primeira = Math.Max(0, (rolagem - Margem) / Celula) * c;
        int ultima = Math.Min(itens.Count - 1, ((rolagem + ClientSize.Height) / Celula + 1) * c);

        for (int i = primeira; i <= ultima; i++)
        {
            Rectangle r = Ret(i);
            if (!r.IntersectsWith(e.ClipRectangle)) continue;
            if (i == selecionado || i == sobre)
            {
                using (GraphicsPath p = Tema.Arredondado(Rectangle.Inflate(r, -2, -2), 6))
                    g.FillPath(Tema.Pincel(i == selecionado ? Tema.Selecao : Tema.Campo), p);
            }
            Bitmap b = EmojiCor.DaGrade(itens[i].Simbolo);
            if (b != null)
                g.DrawImage(b, new Rectangle(r.X + (r.Width - b.Width) / 2, r.Y + (r.Height - b.Height) / 2, b.Width, b.Height));
            else
                TextRenderer.DrawText(g, itens[i].Simbolo, Tema.Emote, r, Tema.Texto, Tema.Centro);
        }
    }
}

// Aba do topo da janela
class BotaoAba : Control
{
    bool ativa, sobre;

    public bool Ativa { get { return ativa; } set { ativa = value; Invalidate(); } }

    public BotaoAba(string texto)
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Text = texto;
        Font = Tema.Aba;
        BackColor = Tema.Fundo;
        Cursor = Cursors.Hand;
        Size = new Size(TextRenderer.MeasureText(texto, Tema.Aba).Width + 28, 54);
    }

    protected override void OnMouseEnter(EventArgs e) { sobre = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { sobre = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width, Height - 4),
            ativa || sobre ? Tema.Texto : Tema.Suave, Tema.Centro);
        if (ativa) e.Graphics.FillRectangle(Tema.Pincel(Tema.Destaque), 14, Height - 10, Width - 28, 3);
    }
}

// Botao liga/desliga
class Interruptor : Control
{
    bool marcado;
    public event EventHandler Mudou;

    public bool Marcado { get { return marcado; } set { marcado = value; Invalidate(); } }

    public Interruptor()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Size = new Size(46, 26);
        BackColor = Tema.Painel;
        Cursor = Cursors.Hand;
    }

    protected override void OnClick(EventArgs e)
    {
        Marcado = !Marcado;
        if (Mudou != null) Mudou(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Arredondado(new Rectangle(1, 2, 42, 21), 10))
            g.FillPath(Tema.Pincel(marcado ? Tema.Destaque : Tema.Campo), p);
        g.FillEllipse(Brushes.White, marcado ? 25 : 5, 6, 14, 14);
    }
}


// ---------------------------------------------------------------------
// 8. JANELA PRINCIPAL (abas)
// ---------------------------------------------------------------------
class Janela : Form
{
    public const int PgHistorico = 0, PgSalvos = 1, PgEmotes = 2, PgConfig = 3;

    public object Resultado;     // Item do historico ou texto (salvo / emote) escolhido
    public int Pagina { get { return pagina; } }

    readonly Nucleo nucleo;
    int pagina = -1;
    bool fechando;

    // Topo e partes comuns
    BotaoAba[] abas;
    Label botaoAcao, botaoConfig, rodape;
    Panel barraBusca;
    TextBox busca;
    Panel[] paginas;
    Panel conteudo;
    ToolTip dicas;
    Timer esperaBusca;           // so filtra quando a digitacao da uma pausa
    bool buscaSilenciosa;        // limpando a busca por codigo: nao agenda filtro
    bool dialogoAberto;          // janela de "Salvar como" aberta: nao fecha ao perder o foco

    // Historico
    ListaCartoes listaHist;
    Label vazioHist, infoHist;
    TextBox previaTexto;
    PictureBox previaImagem;
    Label botaoSalvarImagem, botaoEditarImagem;
    readonly List<Item> visHist = new List<Item>();
    bool confirmarLimpar;

    // Salvos
    ListaCartoes listaSalvos;
    PictureBox previaSalvo;
    Label botaoTirarImagem;
    Bitmap imagemNova;           // imagem escolhida na edicao, ainda nao salva
    bool imagemTirada;
    Label vazioSalvos, infoSalvos;
    TextBox editor;
    Panel barraEdicao;
    readonly List<Salvo> visSalvos = new List<Salvo>();
    bool editando;
    Salvo emEdicao;   // null durante a edicao = texto novo

    // Emotes
    GradeEmotes grade;
    Label[] botoesCat;
    Label statusIcone, statusNome;
    int categoria = Emotes.Recentes;

    // Configuracoes
    Label[] caixasAtalho;
    Label msgConfig;
    Interruptor optColar, optSom, optIniciar;
    Label[] botoesPrazo;
    int gravando = -1;

    // Sombra em volta da janela
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;
            return cp;
        }
    }

    public Janela(Nucleo n, int paginaInicial)
    {
        nucleo = n;
        nucleo.TeclaGravada += TeclaGravada;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        Size            = new Size(920, 580);
        BackColor       = Tema.Borda;
        Padding         = new Padding(1);
        TopMost         = true;
        ShowInTaskbar   = false;
        Text            = "\u00c1rea de transfer\u00eancia";

        Rectangle tela = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        Location = new Point(tela.X + (tela.Width - Width) / 2, tela.Y + (tela.Height - Height) / 2);

        dicas = new ToolTip();

        Panel raiz = NovoPainel(DockStyle.Fill, Tema.Fundo);

        // ---- Cabecalho: abas, botao de acao, engrenagem e fechar ----
        Panel cabecalho = NovoPainel(DockStyle.Top, Tema.Fundo);
        cabecalho.Height = 54;

        string[] nomes = { "Hist\u00f3rico", "Salvos", "Emotes" };
        abas = new BotaoAba[3];
        int x = 8;
        for (int i = 0; i < 3; i++)
        {
            BotaoAba a = new BotaoAba(nomes[i]);
            a.Location = new Point(x, 0);
            x += a.Width;
            int pg = i;
            a.MouseDown += delegate { MostrarPagina(pg); };
            abas[i] = a;
        }

        Label botaoFechar = NovoBotao("r", Tema.Simbolo, 50);
        botaoFechar.MouseEnter += delegate { botaoFechar.BackColor = Tema.Fechar; botaoFechar.ForeColor = Color.White; };
        botaoFechar.MouseLeave += delegate { botaoFechar.BackColor = Tema.Fundo; botaoFechar.ForeColor = Tema.Suave; };
        botaoFechar.MouseDown  += delegate { Fechar(); };
        dicas.SetToolTip(botaoFechar, "Fechar (Esc)");

        botaoConfig = NovoBotao("\u2699", Tema.Engrenagem, 48);
        botaoConfig.MouseEnter += delegate { botaoConfig.BackColor = Tema.Campo; };
        botaoConfig.MouseLeave += delegate { botaoConfig.BackColor = Tema.Fundo; };
        botaoConfig.MouseDown  += delegate { MostrarPagina(pagina == PgConfig ? PgHistorico : PgConfig); };
        dicas.SetToolTip(botaoConfig, "Configura\u00e7\u00f5es");

        botaoAcao = NovoBotao("", Tema.Pequena, 190);
        botaoAcao.MouseEnter += delegate { if (!confirmarLimpar) botaoAcao.ForeColor = Tema.Texto; };
        botaoAcao.MouseLeave += delegate { ResetarLimpar(); };
        botaoAcao.MouseDown  += delegate(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (pagina == PgSalvos) ComecarEdicao(null);
            else if (pagina == PgHistorico) CliqueLimpar();
        };

        MouseEventHandler arrastar = delegate(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Nativo.ReleaseCapture();
            Nativo.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        };
        cabecalho.MouseDown += arrastar;

        cabecalho.Controls.AddRange(abas);
        cabecalho.Controls.AddRange(new Control[] { botaoAcao, botaoConfig, botaoFechar });

        // ---- Busca ----
        barraBusca = NovoPainel(DockStyle.Top, Tema.Fundo);
        barraBusca.Height = 50;
        barraBusca.Padding = new Padding(16, 2, 16, 12);
        Panel caixaBusca = NovoPainel(DockStyle.Fill, Tema.Campo);
        caixaBusca.Padding = new Padding(12, 9, 12, 0);
        busca = new TextBox();
        busca.BorderStyle = BorderStyle.None;
        busca.Dock = DockStyle.Fill;
        busca.BackColor = Tema.Campo;
        busca.ForeColor = Tema.Texto;
        busca.Font = Tema.Normal;
        esperaBusca = new Timer();
        esperaBusca.Interval = 120;
        esperaBusca.Tick += delegate { esperaBusca.Stop(); Filtrar(); };
        busca.TextChanged += delegate
        {
            if (buscaSilenciosa) return;
            esperaBusca.Stop();
            esperaBusca.Start();
        };
        caixaBusca.Controls.Add(busca);
        barraBusca.Controls.Add(caixaBusca);

        // ---- Rodape ----
        rodape = NovoRotulo("", Tema.Pequena, Tema.Suave, Tema.Fundo);
        rodape.Dock = DockStyle.Bottom;
        rodape.Height = 36;
        rodape.TextAlign = ContentAlignment.MiddleLeft;
        rodape.Padding = new Padding(14, 0, 0, 0);

        // ---- Paginas ----
        // Cada pagina so e montada na primeira vez que abre
        conteudo = NovoPainel(DockStyle.Fill, Tema.Fundo);
        conteudo.Padding = new Padding(16, 0, 16, 0);
        paginas = new Panel[4];

        // A ordem importa: o ultimo adicionado fica na borda de fora
        raiz.Controls.AddRange(new Control[] { conteudo, rodape, barraBusca, cabecalho });
        Controls.Add(raiz);

        MostrarPagina(paginaInicial);
    }

    // =================================================================
    // Utilidades de construcao
    // =================================================================
    static Panel NovoPainel(DockStyle dock, Color fundo)
    {
        Panel p = new Panel();
        p.Dock = dock;
        p.BackColor = fundo;
        return p;
    }

    static Label NovoRotulo(string texto, Font fonte, Color cor, Color fundo)
    {
        Label l = new Label();
        l.Text = texto;
        l.Font = fonte;
        l.ForeColor = cor;
        l.BackColor = fundo;
        l.UseMnemonic = false;
        return l;
    }

    static Label NovoBotao(string texto, Font fonte, int largura)
    {
        Label l = NovoRotulo(texto, fonte, Tema.Suave, Tema.Fundo);
        l.Dock = DockStyle.Right;
        l.Width = largura;
        l.TextAlign = ContentAlignment.MiddleCenter;
        l.Cursor = Cursors.Hand;
        return l;
    }

    // Botao retangular dentro das paginas
    static Label BotaoCheio(string texto, Color fundo, Color fundoSob)
    {
        Label l = NovoRotulo(texto, Tema.Negrito, Tema.Texto, fundo);
        l.TextAlign = ContentAlignment.MiddleCenter;
        l.Cursor = Cursors.Hand;
        l.MouseEnter += delegate { if (l.BackColor == fundo) l.BackColor = fundoSob; };
        l.MouseLeave += delegate { if (l.BackColor == fundoSob) l.BackColor = fundo; };
        return l;
    }

    static Label Posicionar(Label l, int x, int y, int w, int h)
    {
        l.Location = new Point(x, y);
        l.Size = new Size(w, h);
        if (l.TextAlign == ContentAlignment.TopLeft) l.TextAlign = ContentAlignment.MiddleLeft;
        return l;
    }

    TextBox NovoTextoPrevia()
    {
        TextBox t = new TextBox();
        t.Multiline = true;
        t.ReadOnly = true;
        t.WordWrap = true;
        t.ScrollBars = ScrollBars.Vertical;
        t.BorderStyle = BorderStyle.None;
        t.Dock = DockStyle.Fill;
        t.BackColor = Tema.Painel;
        t.ForeColor = Tema.Texto;
        t.Font = Tema.Mono;
        t.HandleCreated += delegate { Nativo.TemaEscuro(t.Handle); };
        return t;
    }

    static string Quebras(string t)
    {
        if (t.Length > 200000) t = t.Substring(0, 200000) + "\n...";
        StringBuilder sb = new StringBuilder(t.Length + 64);
        char antes = '\0';
        foreach (char c in t)
        {
            if (c == '\n' && antes != '\r') sb.Append('\r');
            sb.Append(c);
            antes = c;
        }
        return sb.ToString();
    }

    void LimparBusca()
    {
        esperaBusca.Stop();
        buscaSilenciosa = true;
        busca.Text = "";
        buscaSilenciosa = false;
    }

    // Digitar com a lista em foco vai para a busca
    void RedirecionarDigitacao(object s, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar)) return;
        busca.Focus();
        busca.SelectedText = e.KeyChar.ToString();
        e.Handled = true;
    }

    // =================================================================
    // Navegacao entre paginas
    // =================================================================
    public void MostrarPagina(int p)
    {
        if (p == pagina) return;
        if (editando) SalvarEdicao();
        PararGravacao();
        ResetarLimpar();

        if (paginas[p] == null)
        {
            paginas[p] = p == PgHistorico ? CriarHistorico() : p == PgSalvos ? CriarSalvos() :
                         p == PgEmotes ? CriarEmotes() : CriarConfig();
            paginas[p].Visible = false;
            conteudo.Controls.Add(paginas[p]);
        }

        pagina = p;
        for (int i = 0; i < paginas.Length; i++) if (paginas[i] != null) paginas[i].Visible = i == p;
        for (int i = 0; i < abas.Length; i++) abas[i].Ativa = i == p;
        botaoConfig.ForeColor = p == PgConfig ? Tema.Destaque : Tema.Suave;
        barraBusca.Visible = p != PgConfig;
        botaoAcao.Visible = p == PgHistorico || p == PgSalvos;
        botaoAcao.Text = p == PgSalvos ? "+  Novo salvo" : "Limpar tudo";

        switch (p)
        {
            case PgHistorico:
                rodape.Text = "Clique  ver" + Tema.Ponto + "Duplo clique ou Enter  " + AcaoDeUsar() + Tema.Ponto +
                              "Bot\u00e3o direito  mais op\u00e7\u00f5es" + Tema.Ponto + "Ctrl+E  editar imagem" +
                              Tema.Ponto + "Shift+Del  apagar" + Tema.Ponto + "Esc  fechar";
                break;
            case PgSalvos:
                rodape.Text = "Clique  ver" + Tema.Ponto + "Duplo clique ou Enter  " + AcaoDeUsar() + Tema.Ponto +
                              "Bot\u00e3o direito  editar ou apagar" + Tema.Ponto + "Ctrl+S  salvar a edi\u00e7\u00e3o";
                break;
            case PgEmotes:
                rodape.Text = "Clique ou Enter  copiar" + Tema.Ponto + "Setas  navegar" + Tema.Ponto + "Digite para buscar";
                break;
            default:
                rodape.Text = "As mudan\u00e7as s\u00e3o salvas na hora";
                AtualizarConfig();
                break;
        }

        LimparBusca();
        Filtrar();
        AtualizarDicaBusca();
        if (IsHandleCreated && p != PgConfig) busca.Focus();
    }

    static string AcaoDeUsar() { return Config.ColarAuto ? "colar" : "copiar"; }

    void AtualizarDicaBusca()
    {
        if (!busca.IsHandleCreated) return;
        string dica = pagina == PgSalvos ? "Buscar nos textos salvos..." :
                      pagina == PgEmotes ? "Buscar emotes (ex.: cora\u00e7\u00e3o, risada, fogo)..." :
                                           "Buscar no hist\u00f3rico...";
        Nativo.SendMessageTexto(busca.Handle, 0x1501, new IntPtr(1), dica);
    }

    void Filtrar()
    {
        if (pagina == PgHistorico) RecarregarHistorico();
        else if (pagina == PgSalvos) RecarregarSalvos();
        else if (pagina == PgEmotes) RecarregarEmotes();
    }

    // Chamado pelo programa quando algo novo e copiado
    public void Recarregar()
    {
        if (pagina == PgHistorico) RecarregarHistorico();
    }

    public void Fechar()
    {
        if (fechando) return;
        fechando = true;
        BeginInvoke(new MethodInvoker(Close));
    }

    void Escolher(object resultado)
    {
        Resultado = resultado;
        Fechar();
    }

    // =================================================================
    // Pagina: HISTORICO
    // =================================================================
    Panel CriarHistorico()
    {
        Panel pg = NovoPainel(DockStyle.Fill, Tema.Fundo);

        Panel esquerda = NovoPainel(DockStyle.Left, Tema.Painel);
        esquerda.Width = 370;
        listaHist = new ListaCartoes();
        listaHist.Preencher = delegate(int i, Cartao c)
        {
            Item it = visHist[i];
            c.Titulo = it.Titulo;
            c.Info   = Tema.Hora(it.Hora) + Tema.Ponto + it.Info;
            c.Mini   = it.Mini;
            c.Icone  = it.Imagem ? null : "Aa";
        };
        listaHist.SelectedIndexChanged += delegate { PreviaHistorico(); };
        // Um clique so seleciona e mostra a previa; dois cliques usam o item
        listaHist.MouseDoubleClick += delegate(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int i = listaHist.IndexFromPoint(e.Location);
            if (i >= 0 && i < visHist.Count) Escolher(visHist[i]);
        };
        listaHist.KeyPress += RedirecionarDigitacao;
        listaHist.ContextMenuStrip = MenuHistorico();

        vazioHist = NovoRotulo("", Tema.Normal, Tema.Suave, Tema.Painel);
        vazioHist.Dock = DockStyle.Fill;
        vazioHist.TextAlign = ContentAlignment.MiddleCenter;
        esquerda.Controls.AddRange(new Control[] { listaHist, vazioHist });

        Panel espaco = NovoPainel(DockStyle.Left, Tema.Fundo);
        espaco.Width = 12;

        Panel direita = NovoPainel(DockStyle.Fill, Tema.Painel);
        direita.Padding = new Padding(16, 10, 10, 12);
        infoHist = NovoRotulo("", Tema.Pequena, Tema.Suave, Tema.Painel);
        infoHist.Dock = DockStyle.Top;
        infoHist.Height = 30;
        infoHist.TextAlign = ContentAlignment.MiddleLeft;

        botaoSalvarImagem = BotaoCheio("Salvar como...", Tema.Campo, Tema.CampoSob);
        botaoSalvarImagem.Font = Tema.Pequena;
        botaoSalvarImagem.Dock = DockStyle.Right;
        botaoSalvarImagem.Width = 110;
        botaoSalvarImagem.Visible = false;
        botaoSalvarImagem.MouseClick += delegate(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) SalvarImagemComo();
        };
        dicas.SetToolTip(botaoSalvarImagem, "Salvar esta imagem como arquivo no computador (Ctrl+S)");
        infoHist.Controls.Add(botaoSalvarImagem);

        botaoEditarImagem = BotaoCheio("Editar", Tema.Destaque, Color.FromArgb(156, 116, 250));
        botaoEditarImagem.Font = Tema.Pequena;
        botaoEditarImagem.Dock = DockStyle.Right;
        botaoEditarImagem.Width = 76;
        botaoEditarImagem.Visible = false;
        botaoEditarImagem.MouseClick += delegate(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) EditarImagem();
        };
        dicas.SetToolTip(botaoEditarImagem, "Abrir em tela cheia para rabiscar, borrar, apagar ou colocar outra imagem por cima (Ctrl+E)");
        infoHist.Controls.Add(botaoEditarImagem);

        previaTexto = NovoTextoPrevia();
        previaImagem = new PictureBox();
        previaImagem.Dock = DockStyle.Fill;
        previaImagem.SizeMode = PictureBoxSizeMode.Zoom;
        previaImagem.BackColor = Tema.Painel;
        previaImagem.Visible = false;
        previaImagem.Cursor = Cursors.Hand;
        previaImagem.MouseDoubleClick += delegate(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) EditarImagem();
        };
        dicas.SetToolTip(previaImagem, "Dois cliques para editar em tela cheia");
        direita.Controls.AddRange(new Control[] { previaTexto, previaImagem, infoHist });

        pg.Controls.AddRange(new Control[] { direita, espaco, esquerda });
        return pg;
    }

    ContextMenuStrip MenuHistorico()
    {
        ContextMenuStrip m = CoresMenu.NovoMenu();
        ToolStripMenuItem copiar = new ToolStripMenuItem("Copiar");
        ToolStripMenuItem salvar = new ToolStripMenuItem("Salvar na aba Salvos");
        ToolStripMenuItem salvarArquivo = new ToolStripMenuItem("Salvar imagem como...");
        ToolStripMenuItem editar = new ToolStripMenuItem("Editar imagem");
        ToolStripMenuItem apagar = new ToolStripMenuItem("Apagar");
        salvarArquivo.Click += delegate { SalvarImagemComo(); };
        editar.Click += delegate { EditarImagem(); };
        copiar.Click += delegate { Item it = ItemHistAtual(); if (it != null) Escolher(it); };
        salvar.Click += delegate
        {
            Item it = ItemHistAtual();
            if (it == null) return;
            try
            {
                if (!it.Imagem) Salvos.Adicionar(it.Texto);
                else using (Bitmap img = Historico.LerImagem(it.Caminho)) Salvos.Adicionar("", img);
                infoHist.Text = it.Imagem ? "\u2713  Salva. Edite na aba Salvos para colocar o texto"
                                          : "\u2713  Salvo na aba Salvos";
            }
            catch (Exception ex)
            {
                App.Log("Erro ao salvar item: " + ex.Message);
                infoHist.Text = "N\u00e3o consegui salvar.";
            }
        };
        apagar.Click += delegate { ApagarHistorico(); };
        m.Items.AddRange(new ToolStripItem[] { copiar, editar, salvar, salvarArquivo, new ToolStripSeparator(), apagar });
        m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
        {
            Item it = ItemHistAtual();
            if (it == null) { e.Cancel = true; return; }
            salvarArquivo.Visible = it.Imagem;
            editar.Visible = it.Imagem;
        };
        return m;
    }

    // A imagem e lida antes de fechar: se o arquivo sumiu, o aviso ainda aparece
    void EditarImagem()
    {
        Item it = ItemHistAtual();
        if (it == null || !it.Imagem) return;
        Bitmap img;
        try { img = Historico.LerImagem(it.Caminho); }
        catch (Exception ex)
        {
            App.Log("Erro ao abrir no editor: " + ex.Message);
            infoHist.Text = "N\u00e3o consegui abrir esta imagem.";
            return;
        }
        Fechar();
        App.Editar(img);
    }

    // Abre "Salvar como" para a imagem selecionada no historico
    void SalvarImagemComo()
    {
        Item it = ItemHistAtual();
        if (it == null || !it.Imagem) return;

        dialogoAberto = true;
        try
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Salvar imagem como";
                d.Filter = "Imagem PNG (*.png)|*.png|Imagem JPG (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp";
                d.FileName = "imagem-" + it.Hora.ToString("yyyy-MM-dd_HH-mm-ss") + ".png";
                d.DefaultExt = "png";
                d.AddExtension = true;
                d.OverwritePrompt = true;
                d.RestoreDirectory = true;
                if (Directory.Exists(Config.PastaPrints)) d.InitialDirectory = Config.PastaPrints;
                if (d.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    Historico.SalvarComo(it, d.FileName);
                    infoHist.Text = "\u2713  Salvo em " + d.FileName;
                }
                catch (Exception ex)
                {
                    App.Log("Erro ao salvar imagem: " + ex.Message);
                    infoHist.Text = "N\u00e3o consegui salvar: " + ex.Message;
                }
            }
        }
        finally
        {
            dialogoAberto = false;
            Activate();
            busca.Focus();
            App.Aparar();
        }
    }

    Item ItemHistAtual()
    {
        int i = listaHist.SelectedIndex;
        return i >= 0 && i < visHist.Count ? visHist[i] : null;
    }

    void RecarregarHistorico()
    {
        string filtro = busca.Text.Trim();
        visHist.Clear();
        foreach (Item it in Historico.Itens)
        {
            bool passa = filtro.Length == 0 ||
                (it.Imagem ? "imagem".IndexOf(filtro, StringComparison.OrdinalIgnoreCase) >= 0 : it.Contem(filtro));
            if (passa) visHist.Add(it);
        }
        listaHist.Definir(visHist.Count);

        bool tem = visHist.Count > 0;
        listaHist.Visible = tem;
        vazioHist.Visible = !tem;
        vazioHist.Text = filtro.Length > 0 ? "Nenhum resultado" : "Nada copiado ainda. Copie um texto ou uma imagem.";

        if (tem) listaHist.SelectedIndex = 0;
        else PreviaHistorico();
    }

    void PreviaHistorico()
    {
        Image velha = previaImagem.Image;
        previaImagem.Image = null;
        if (velha != null) velha.Dispose();

        Item it = ItemHistAtual();
        botaoSalvarImagem.Visible = botaoEditarImagem.Visible = it != null && it.Imagem;
        if (it == null)
        {
            infoHist.Text = "";
            previaTexto.Text = "";
            previaTexto.Visible = true;
            previaImagem.Visible = false;
            return;
        }

        if (it.Imagem)
        {
            try { previaImagem.Image = Historico.LerPrevia(it.Caminho, 520, 460); } catch { }
            previaImagem.Visible = true;
            previaTexto.Visible = false;
            infoHist.Text = "IMAGEM" + Tema.Ponto + it.Info + Tema.Ponto + Tema.Hora(it.Hora);
        }
        else
        {
            previaTexto.Text = Quebras(it.Texto);
            previaTexto.Visible = true;
            previaImagem.Visible = false;
            infoHist.Text = "TEXTO" + Tema.Ponto + it.Info + Tema.Ponto + Tema.Hora(it.Hora);
        }
    }

    void ApagarHistorico()
    {
        int i = listaHist.SelectedIndex;
        if (i < 0 || i >= visHist.Count) return;
        Historico.Remover(visHist[i]);
        RecarregarHistorico();
        if (listaHist.Items.Count > 0) listaHist.SelectedIndex = Math.Min(i, listaHist.Items.Count - 1);
    }

    // "Limpar tudo": o primeiro clique pede confirmacao, o segundo apaga
    void CliqueLimpar()
    {
        if (confirmarLimpar)
        {
            ResetarLimpar();
            Historico.LimparTudo();
            RecarregarHistorico();
            App.Aparar();
        }
        else
        {
            confirmarLimpar = true;
            botaoAcao.Text = "Clique de novo para apagar";
            botaoAcao.ForeColor = Tema.Perigo;
        }
    }

    void ResetarLimpar()
    {
        confirmarLimpar = false;
        if (botaoAcao == null) return;
        botaoAcao.ForeColor = Tema.Suave;
        if (pagina == PgHistorico) botaoAcao.Text = "Limpar tudo";
    }

    // =================================================================
    // Pagina: SALVOS
    // =================================================================
    Panel CriarSalvos()
    {
        Panel pg = NovoPainel(DockStyle.Fill, Tema.Fundo);

        Panel esquerda = NovoPainel(DockStyle.Left, Tema.Painel);
        esquerda.Width = 370;
        listaSalvos = new ListaCartoes();
        listaSalvos.Preencher = delegate(int i, Cartao c)
        {
            Salvo s = visSalvos[i];
            c.Titulo = s.Titulo;
            c.Info = !s.TemImagem ? s.Texto.Length + " caracteres" :
                     s.TemTexto ? "Imagem + " + s.Texto.Length + " caracteres" : "S\u00f3 imagem";
            if (s.TemImagem) c.Mini = s.Foto.Mini;
            if (c.Mini == null)
            {
                c.Icone = "\u2605";
                c.FonteIcone = Tema.Engrenagem;
            }
        };
        listaSalvos.SelectedIndexChanged += delegate { if (!editando) PreviaSalvo(); };
        listaSalvos.MouseDoubleClick += delegate(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int i = listaSalvos.IndexFromPoint(e.Location);
            if (i < 0 || i >= visSalvos.Count) return;
            Salvo alvo = visSalvos[i];   // a edicao pode mudar a ordem da lista
            if (editando) SalvarEdicao();
            Escolher(Envio(alvo));
        };
        listaSalvos.KeyPress += RedirecionarDigitacao;
        listaSalvos.ContextMenuStrip = MenuSalvos();

        vazioSalvos = NovoRotulo("", Tema.Normal, Tema.Suave, Tema.Painel);
        vazioSalvos.Dock = DockStyle.Fill;
        vazioSalvos.TextAlign = ContentAlignment.MiddleCenter;
        esquerda.Controls.AddRange(new Control[] { listaSalvos, vazioSalvos });

        Panel espaco = NovoPainel(DockStyle.Left, Tema.Fundo);
        espaco.Width = 12;

        Panel direita = NovoPainel(DockStyle.Fill, Tema.Painel);
        direita.Padding = new Padding(16, 10, 10, 12);
        infoSalvos = NovoRotulo("", Tema.Pequena, Tema.Suave, Tema.Painel);
        infoSalvos.Dock = DockStyle.Top;
        infoSalvos.Height = 30;
        infoSalvos.TextAlign = ContentAlignment.MiddleLeft;

        editor = NovoTextoPrevia();
        editor.Font = Tema.Normal;
        editor.AcceptsTab = true;

        barraEdicao = NovoPainel(DockStyle.Bottom, Tema.Painel);
        barraEdicao.Height = 46;
        barraEdicao.Padding = new Padding(0, 10, 0, 0);
        Label salvar = BotaoCheio("Salvar", Tema.Destaque, Color.FromArgb(156, 116, 250));
        salvar.Dock = DockStyle.Left;
        salvar.Width = 110;
        salvar.MouseDown += delegate { SalvarEdicao(); };
        Panel vao = NovoPainel(DockStyle.Left, Tema.Painel);
        vao.Width = 8;
        Label cancelar = BotaoCheio("Cancelar", Tema.Campo, Tema.CampoSob);
        cancelar.Dock = DockStyle.Left;
        cancelar.Width = 110;
        cancelar.MouseDown += delegate { CancelarEdicao(); };
        Panel vao2 = NovoPainel(DockStyle.Left, Tema.Painel);
        vao2.Width = 8;
        Label imagem = BotaoCheio("Imagem", Tema.Campo, Tema.CampoSob);
        imagem.Dock = DockStyle.Left;
        imagem.Width = 110;
        imagem.MouseDown += delegate { MenuImagemSalvo(imagem); };
        dicas.SetToolTip(imagem, "Colocar uma imagem junto com o texto. Ctrl+V cola uma imagem copiada");
        Panel vao3 = NovoPainel(DockStyle.Left, Tema.Painel);
        vao3.Width = 8;
        botaoTirarImagem = BotaoCheio("Tirar imagem", Tema.Campo, Tema.CampoSob);
        botaoTirarImagem.Dock = DockStyle.Left;
        botaoTirarImagem.Width = 110;
        botaoTirarImagem.MouseDown += delegate { TirarImagemSalvo(); };
        barraEdicao.Controls.AddRange(new Control[] { botaoTirarImagem, vao3, imagem, vao2, cancelar, vao, salvar });
        barraEdicao.Visible = false;

        previaSalvo = new PictureBox();
        previaSalvo.Dock = DockStyle.Top;
        previaSalvo.Height = 210;
        previaSalvo.SizeMode = PictureBoxSizeMode.Zoom;
        previaSalvo.BackColor = Tema.Painel;
        previaSalvo.Visible = false;

        direita.Controls.AddRange(new Control[] { editor, barraEdicao, previaSalvo, infoSalvos });
        pg.Controls.AddRange(new Control[] { direita, espaco, esquerda });
        return pg;
    }

    ContextMenuStrip MenuSalvos()
    {
        ContextMenuStrip m = CoresMenu.NovoMenu();
        ToolStripMenuItem copiar = new ToolStripMenuItem("Copiar");
        ToolStripMenuItem soTexto = new ToolStripMenuItem("Copiar s\u00f3 o texto");
        ToolStripMenuItem soImagem = new ToolStripMenuItem("Copiar s\u00f3 a imagem");
        ToolStripMenuItem editar = new ToolStripMenuItem("Editar");
        ToolStripMenuItem apagar = new ToolStripMenuItem("Apagar");
        copiar.Click += delegate { Salvo s = SalvoAtual(); if (s != null) Escolher(Envio(s)); };
        soTexto.Click += delegate { Salvo s = SalvoAtual(); if (s != null) Escolher(s.Texto); };
        soImagem.Click += delegate { Salvo s = SalvoAtual(); if (s != null) Escolher(new EnvioSalvo(s, false)); };
        editar.Click += delegate { Salvo s = SalvoAtual(); if (s != null) ComecarEdicao(s); };
        apagar.Click += delegate { ApagarSalvo(); };
        m.Items.AddRange(new ToolStripItem[] { copiar, soTexto, soImagem, editar, new ToolStripSeparator(), apagar });
        m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
        {
            Salvo atual = SalvoAtual();
            if (atual == null) { e.Cancel = true; return; }
            soTexto.Visible = soImagem.Visible = atual.TemImagem && atual.TemTexto;
            copiar.Text = atual.TemImagem && atual.TemTexto ? "Colar imagem e texto" : "Copiar";
        };
        return m;
    }

    // Salvo com imagem entrega os dois de uma vez; so com texto, segue como sempre
    static object Envio(Salvo s) { return s.TemImagem ? (object)new EnvioSalvo(s, s.TemTexto) : s.Texto; }

    Salvo SalvoAtual()
    {
        int i = listaSalvos.SelectedIndex;
        return i >= 0 && i < visSalvos.Count ? visSalvos[i] : null;
    }

    void RecarregarSalvos()
    {
        string filtro = Tema.Dobrar(busca.Text.Trim());
        visSalvos.Clear();
        foreach (Salvo s in Salvos.Itens)
            if (filtro.Length == 0 || Tema.Dobrar(s.Texto).Contains(filtro)) visSalvos.Add(s);
        listaSalvos.Definir(visSalvos.Count);

        bool tem = visSalvos.Count > 0;
        listaSalvos.Visible = tem;
        vazioSalvos.Visible = !tem;
        vazioSalvos.Text = filtro.Length > 0 ? "Nenhum resultado" : "Nada salvo ainda.\nClique em \"+ Novo salvo\" no topo.";

        if (tem) listaSalvos.SelectedIndex = 0;
        else if (!editando) PreviaSalvo();
    }

    void PreviaSalvo()
    {
        Salvo s = SalvoAtual();
        editor.Text = s == null ? "" : Quebras(s.Texto);
        Bitmap b = null;
        if (s != null && s.TemImagem)
            try { b = Historico.LerPrevia(s.Foto.Caminho, 520, 210); }
            catch (Exception ex) { App.Log("Erro ao abrir imagem do salvo: " + ex.Message); }
        TrocarPreviaSalvo(b);
        infoSalvos.Text = s == null ? "" :
            !s.TemImagem ? "TEXTO SALVO" + Tema.Ponto + s.Texto.Length + " caracteres" + Tema.Ponto + "bot\u00e3o direito para editar" :
            s.TemTexto ? "IMAGEM E TEXTO" + Tema.Ponto + "duplo clique cola os dois" + Tema.Ponto + "bot\u00e3o direito para editar" :
            "IMAGEM SALVA" + Tema.Ponto + "bot\u00e3o direito para editar";
    }

    void TrocarPreviaSalvo(Bitmap nova)
    {
        Image velha = previaSalvo.Image;
        previaSalvo.Image = nova;
        previaSalvo.Visible = nova != null;
        if (velha != null) velha.Dispose();
    }

    void MenuImagemSalvo(Control perto)
    {
        ContextMenuStrip m = CoresMenu.NovoMenu();
        ToolStripMenuItem arquivo = new ToolStripMenuItem("Escolher arquivo...");
        arquivo.Click += delegate { ImagemSalvoDeArquivo(); };
        ToolStripMenuItem colar = new ToolStripMenuItem("Colar imagem copiada (Ctrl+V)");
        colar.Click += delegate { ColarImagemSalvo(); };
        colar.Enabled = Clipboard.ContainsImage();
        m.Items.AddRange(new ToolStripItem[] { arquivo, colar });
        m.Closed += delegate { BeginInvoke(new MethodInvoker(m.Dispose)); };
        m.Show(perto, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
    }

    void ImagemSalvoDeArquivo()
    {
        dialogoAberto = true;
        try
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Escolher imagem";
                d.Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Todos os arquivos|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { AnexarImagem(Historico.LerImagem(d.FileName)); }
                catch (Exception ex)
                {
                    App.Log("Erro ao abrir imagem para o salvo: " + ex.Message);
                    infoSalvos.Text = "N\u00e3o consegui abrir essa imagem.";
                }
            }
        }
        finally { dialogoAberto = false; }
    }

    void ColarImagemSalvo()
    {
        try
        {
            Image img = Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
            if (img == null) return;
            AnexarImagem(img as Bitmap ?? new Bitmap(img));
        }
        catch (Exception ex) { App.Log("Erro ao colar imagem no salvo: " + ex.Message); }
    }

    // A imagem so vai para o disco ao salvar: Cancelar nao muda nada
    void AnexarImagem(Bitmap img)
    {
        if (!editando) { img.Dispose(); return; }
        if (imagemNova != null) imagemNova.Dispose();
        imagemNova = img;
        imagemTirada = false;
        TrocarPreviaSalvo(new Bitmap(img));
        botaoTirarImagem.Visible = true;
    }

    void TirarImagemSalvo()
    {
        if (!editando) return;
        if (imagemNova != null) { imagemNova.Dispose(); imagemNova = null; }
        imagemTirada = true;
        TrocarPreviaSalvo(null);
        botaoTirarImagem.Visible = false;
    }

    void ComecarEdicao(Salvo s)
    {
        if (editando) SalvarEdicao();
        editando = true;
        emEdicao = s;
        editor.ReadOnly = false;
        editor.BackColor = Tema.Campo;
        editor.Text = s == null ? "" : Quebras(s.Texto);
        imagemTirada = false;
        if (s == null) TrocarPreviaSalvo(null);
        botaoTirarImagem.Visible = s != null && s.TemImagem;
        infoSalvos.Text = s == null ? "NOVO SALVO" + Tema.Ponto + "escreva, coloque uma imagem se quiser e clique em Salvar (Ctrl+S)"
                                    : "EDITANDO" + Tema.Ponto + "Ctrl+S salva" + Tema.Ponto + "Esc cancela";
        barraEdicao.Visible = true;
        editor.Focus();
        editor.SelectionStart = editor.TextLength;
    }

    void SalvarEdicao()
    {
        if (!editando) return;
        string t = editor.Text;
        Salvo alvo = emEdicao;
        Bitmap nova = imagemNova;
        bool tirar = imagemTirada;
        imagemNova = null;
        SairDaEdicao();

        bool teraImagem = nova != null || (alvo != null && alvo.TemImagem && !tirar);
        string erro = null;
        if (t.Trim().Length > 0 || teraImagem)
        {
            try
            {
                if (alvo == null) alvo = Salvos.Adicionar(t, nova);
                else
                {
                    Salvos.Alterar(alvo, t);
                    if (nova != null) Salvos.DefinirImagem(alvo, nova);
                    else if (tirar) Salvos.DefinirImagem(alvo, null);
                }
            }
            catch (Exception ex)
            {
                App.Log("Erro ao salvar: " + ex.Message);
                erro = "N\u00e3o consegui salvar: " + ex.Message;
            }
        }
        if (nova != null) nova.Dispose();
        RecarregarSalvos();
        int i = alvo == null ? -1 : visSalvos.IndexOf(alvo);
        if (i >= 0) listaSalvos.SelectedIndex = i;
        PreviaSalvo();
        if (erro != null) infoSalvos.Text = erro;
    }

    void CancelarEdicao()
    {
        if (!editando) return;
        SairDaEdicao();
        PreviaSalvo();
    }

    void SairDaEdicao()
    {
        editando = false;
        emEdicao = null;
        editor.ReadOnly = true;
        editor.BackColor = Tema.Painel;
        barraEdicao.Visible = false;
        if (imagemNova != null) { imagemNova.Dispose(); imagemNova = null; }
        imagemTirada = false;
    }

    void ApagarSalvo()
    {
        Salvo s = SalvoAtual();
        if (s == null) return;
        int i = listaSalvos.SelectedIndex;
        Salvos.Remover(s);
        RecarregarSalvos();
        if (listaSalvos.Items.Count > 0) listaSalvos.SelectedIndex = Math.Min(i, listaSalvos.Items.Count - 1);
    }

    // =================================================================
    // Pagina: EMOTES
    // =================================================================
    Panel CriarEmotes()
    {
        Panel pg = NovoPainel(DockStyle.Fill, Tema.Painel);

        // Categorias (Recentes + 9)
        Panel cats = NovoPainel(DockStyle.Top, Tema.Painel);
        cats.Height = 48;
        cats.Padding = new Padding(8, 4, 8, 4);
        botoesCat = new Label[10];
        for (int k = 9; k >= 0; k--)   // ao contrario: o ultimo adicionado fica mais a esquerda
        {
            int cat = k == 0 ? Emotes.Recentes : k - 1;
            string icone = k == 0 ? "\U0001F552" : Emotes.IconesCategorias[k - 1];
            Label b = NovoRotulo("", Tema.Normal, Tema.Suave, Tema.Painel);
            b.Dock = DockStyle.Left;
            b.Width = 46;
            b.Image = EmojiCor.Criar(icone, 30, 20f);
            b.ImageAlign = ContentAlignment.MiddleCenter;
            if (b.Image == null) { b.Text = icone; b.TextAlign = ContentAlignment.MiddleCenter; }
            b.Cursor = Cursors.Hand;
            b.MouseDown += delegate { MostrarCategoria(cat); };
            dicas.SetToolTip(b, k == 0 ? "Recentes" : Emotes.Categorias[k - 1]);
            botoesCat[k] = b;
            cats.Controls.Add(b);
        }

        // Rodape da aba: emote destacado em tamanho grande + nome
        Panel status = NovoPainel(DockStyle.Bottom, Tema.Painel);
        status.Height = 54;
        status.Padding = new Padding(10, 0, 10, 0);
        Panel linha = NovoPainel(DockStyle.Top, Tema.Fundo);
        linha.Height = 1;
        statusIcone = NovoRotulo("", Tema.Emote, Tema.Texto, Tema.Painel);
        statusIcone.Dock = DockStyle.Left;
        statusIcone.Width = 60;
        statusIcone.ImageAlign = ContentAlignment.MiddleCenter;
        statusIcone.TextAlign = ContentAlignment.MiddleCenter;
        statusNome = NovoRotulo("", Tema.Normal, Tema.Texto, Tema.Painel);
        statusNome.Dock = DockStyle.Fill;
        statusNome.TextAlign = ContentAlignment.MiddleLeft;
        status.Controls.AddRange(new Control[] { statusNome, statusIcone, linha });

        grade = new GradeEmotes();
        grade.Dock = DockStyle.Fill;
        grade.Destacado += MostrarEmote;
        grade.Escolhido += delegate(Emote e)
        {
            Emotes.MarcarUsado(e.Simbolo);
            Escolher(e.Simbolo);
        };
        grade.KeyPress += RedirecionarDigitacao;

        pg.Controls.AddRange(new Control[] { grade, status, cats });
        return pg;
    }

    void MostrarCategoria(int cat)
    {
        categoria = cat;
        LimparBusca();
        RecarregarEmotes();
    }

    void RecarregarEmotes()
    {
        bool buscando = busca.Text.Trim().Length > 0;
        List<Emote> lista = Emotes.Filtrar(categoria, busca.Text);
        if (!buscando && categoria == Emotes.Recentes && lista.Count == 0)
        {
            categoria = 0;   // ainda nao tem recentes: abre nas carinhas
            lista = Emotes.Filtrar(categoria, "");
        }
        grade.Definir(lista);

        for (int k = 0; k < botoesCat.Length; k++)
        {
            int cat = k == 0 ? Emotes.Recentes : k - 1;
            bool ativo = !buscando && cat == categoria;
            botoesCat[k].BackColor = ativo ? Tema.Selecao : Tema.Painel;
            botoesCat[k].ForeColor = ativo ? Tema.Texto : Tema.Suave;
        }
        if (lista.Count == 0) statusNome.Text = "Nenhum emote encontrado";
    }

    void MostrarEmote(Emote e)
    {
        Image velha = statusIcone.Image;
        statusIcone.Image = null;
        if (velha != null) velha.Dispose();

        if (e == null) { statusIcone.Text = ""; statusNome.Text = ""; return; }
        statusIcone.Image = EmojiCor.Criar(e.Simbolo, 46, 31f);
        statusIcone.Text = statusIcone.Image == null ? e.Simbolo : "";
        statusNome.Text = e.Nome.Length > 0 ? char.ToUpper(e.Nome[0]) + e.Nome.Substring(1) : "";
    }

    // =================================================================
    // Pagina: CONFIGURACOES
    // =================================================================
    Panel CriarConfig()
    {
        Panel pg = NovoPainel(DockStyle.Fill, Tema.Painel);
        pg.AutoScroll = true;
        int y = 16;

        pg.Controls.Add(Posicionar(NovoRotulo("Atalhos", Tema.Titulo, Tema.Texto, Tema.Painel), 24, y, 400, 30));
        y += 42;

        caixasAtalho = new Label[Config.Atalhos.Length];
        for (int i = 0; i < Config.Atalhos.Length; i++)
        {
            pg.Controls.Add(Posicionar(NovoRotulo(Config.NomesAcoes[i], Tema.Normal, Tema.Texto, Tema.Painel), 24, y, 290, 38));

            Label caixa = NovoRotulo("", Tema.Negrito, Tema.Texto, Tema.Campo);
            Posicionar(caixa, 320, y, 300, 38);
            caixa.TextAlign = ContentAlignment.MiddleCenter;
            caixa.Cursor = Cursors.Hand;
            int idx = i;
            caixa.MouseEnter += delegate { caixa.BackColor = Tema.CampoSob; };
            caixa.MouseLeave += delegate { caixa.BackColor = Tema.Campo; };
            caixa.MouseDown  += delegate { IniciarGravacao(idx); };
            dicas.SetToolTip(caixa, "Clique e aperte a nova combina\u00e7\u00e3o");
            caixasAtalho[i] = caixa;
            pg.Controls.Add(caixa);
            y += 48;
        }

        pg.Controls.Add(Posicionar(NovoRotulo(
            "Clique numa caixa e aperte a nova combina\u00e7\u00e3o." + Tema.Ponto + "Esc cancela" + Tema.Ponto + "Backspace remove o atalho",
            Tema.Pequena, Tema.Suave, Tema.Painel), 24, y, 640, 22));
        y += 26;

        msgConfig = Posicionar(NovoRotulo("", Tema.Pequena, Tema.Suave, Tema.Painel), 24, y, 640, 22);
        pg.Controls.Add(msgConfig);
        y += 30;

        Label restaurar = BotaoCheio("Restaurar atalhos padr\u00e3o", Tema.Campo, Tema.CampoSob);
        Posicionar(restaurar, 24, y, 230, 34);
        restaurar.TextAlign = ContentAlignment.MiddleCenter;
        restaurar.MouseDown += delegate
        {
            PararGravacao();
            Config.Atalhos = (Atalho[])Config.Padrao.Clone();
            AplicarAtalhos("Atalhos padr\u00e3o restaurados.", false);
        };
        pg.Controls.Add(restaurar);
        y += 60;

        pg.Controls.Add(Posicionar(NovoRotulo("Op\u00e7\u00f5es", Tema.Titulo, Tema.Texto, Tema.Painel), 24, y, 400, 30));
        y += 42;

        optColar   = LinhaOpcao(pg, ref y, "Colar sozinho ao escolher um item (al\u00e9m de copiar)");
        optSom     = LinhaOpcao(pg, ref y, "Tocar um som ao tirar print");
        optIniciar = LinhaOpcao(pg, ref y, "Iniciar junto com o Windows");
        optColar.Mudou   += delegate { Config.ColarAuto = optColar.Marcado; Config.Salvar(); };
        optSom.Mudou     += delegate { Config.SomAoCapturar = optSom.Marcado; Config.Salvar(); };
        optIniciar.Mudou += delegate { App.DefinirIniciar(optIniciar.Marcado); };
        y += 6;

        pg.Controls.Add(Posicionar(NovoRotulo("Apagar do hist\u00f3rico depois de", Tema.Normal, Tema.Texto, Tema.Painel), 24, y, 240, 32));
        botoesPrazo = new Label[Config.Prazos.Length];
        for (int i = 0; i < Config.Prazos.Length; i++)
        {
            int horas = Config.Prazos[i];
            Label b = BotaoCheio(Config.NomesPrazos[i], Tema.Campo, Tema.CampoSob);
            b.Font = Tema.Pequena;
            Posicionar(b, 270 + i * 70, y, 66, 32);
            b.TextAlign = ContentAlignment.MiddleCenter;
            b.MouseDown += delegate
            {
                Config.PrazoHoras = horas;
                Config.Salvar();
                AtualizarConfig();
                App.PrazoMudou();
            };
            botoesPrazo[i] = b;
            pg.Controls.Add(b);
        }
        y += 36;
        pg.Controls.Add(Posicionar(NovoRotulo(
            "Vale para textos e imagens do hist\u00f3rico. Os textos salvos e a pasta de prints n\u00e3o s\u00e3o apagados.",
            Tema.Pequena, Tema.Suave, Tema.Painel), 24, y, 640, 20));
        y += 34;

        pg.Controls.Add(LinhaPasta(24, ref y, "Prints: ", Config.PastaPrints));
        pg.Controls.Add(LinhaPasta(24, ref y, "Hist\u00f3rico: ", Config.Pasta));
        pg.Controls.Add(LinhaPasta(24, ref y, "Salvos e configura\u00e7\u00f5es: ", Config.PastaDados));

        return pg;
    }

    // Caminho clicavel: abre a pasta no Explorer
    Label LinhaPasta(int x, ref int y, string nome, string pasta)
    {
        string texto = nome + pasta;
        Label l = NovoRotulo(texto, Tema.Pequena, Tema.Suave, Tema.Painel);
        Posicionar(l, x, y, TextRenderer.MeasureText(texto, Tema.Pequena).Width + 8, 20);
        l.Cursor = Cursors.Hand;
        l.MouseEnter += delegate { l.ForeColor = Tema.Destaque; };
        l.MouseLeave += delegate { l.ForeColor = Tema.Suave; };
        l.MouseDown  += delegate(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) App.AbrirPasta(pasta);
        };
        dicas.SetToolTip(l, "Clique para abrir a pasta");
        y += 22;
        return l;
    }

    Interruptor LinhaOpcao(Panel pg, ref int y, string texto)
    {
        pg.Controls.Add(Posicionar(NovoRotulo(texto, Tema.Normal, Tema.Texto, Tema.Painel), 24, y, 500, 32));
        Interruptor o = new Interruptor();
        o.Location = new Point(574, y + 3);
        pg.Controls.Add(o);
        y += 42;
        return o;
    }

    void AtualizarConfig()
    {
        for (int i = 0; i < caixasAtalho.Length; i++)
        {
            Atalho a = Config.Atalhos[i];
            caixasAtalho[i].Text = a.ToString();
            caixasAtalho[i].ForeColor = a.Vazio ? Tema.Suave : Tema.Texto;
        }
        optColar.Marcado   = Config.ColarAuto;
        optSom.Marcado     = Config.SomAoCapturar;
        optIniciar.Marcado = App.IniciaComWindows();
        for (int i = 0; i < botoesPrazo.Length; i++)
        {
            bool ativo = Config.Prazos[i] == Config.PrazoHoras;
            botoesPrazo[i].BackColor = ativo ? Tema.Destaque : Tema.Campo;
            botoesPrazo[i].ForeColor = ativo ? Color.White : Tema.Texto;
        }
    }

    void IniciarGravacao(int i)
    {
        PararGravacao();
        gravando = i;
        caixasAtalho[i].Text = "Aperte as teclas...";
        caixasAtalho[i].ForeColor = Tema.Destaque;
        msgConfig.Text = "";
        nucleo.Gravando = true;
    }

    void PararGravacao()
    {
        if (gravando < 0) return;
        gravando = -1;
        nucleo.Gravando = false;
        AtualizarConfig();
    }

    void Mensagem(string texto, bool erro)
    {
        msgConfig.Text = texto;
        msgConfig.ForeColor = erro ? Tema.Perigo : Tema.Sucesso;
    }

    // Recebe a combinacao apertada (vinda do gancho de teclado)
    void TeclaGravada(Atalho a)
    {
        if (gravando < 0) return;
        int i = gravando;

        if (a.Vk == 0x1B && a.Mods == 0) { PararGravacao(); return; }   // Esc

        if (a.Vk == 0x08 && a.Mods == 0)                               // Backspace
        {
            Config.Atalhos[i] = new Atalho(0, 0);
            AplicarAtalhos("Atalho removido.", false);
            return;
        }

        if ((a.Mods & ~Atalho.Shift) == 0 && !Atalho.PodeSozinha(a.Vk))
        {
            PararGravacao();
            Mensagem("Use junto com Ctrl, Alt ou Win (ex.: Ctrl + Alt + " + Atalho.NomeTecla(a.Vk) + ").", true);
            return;
        }

        for (int j = 0; j < Config.Atalhos.Length; j++)
        {
            if (j != i && Config.Atalhos[j].Igual(a))
            {
                PararGravacao();
                Mensagem(a + " j\u00e1 est\u00e1 em: " + Config.NomesAcoes[j], true);
                return;
            }
        }

        Config.Atalhos[i] = a;
        AplicarAtalhos("Salvo: " + Config.NomesAcoes[i] + " = " + a, false);
    }

    void AplicarAtalhos(string mensagem, bool erro)
    {
        gravando = -1;
        nucleo.Gravando = false;
        Config.Salvar();
        nucleo.RegistrarReservas();
        AtualizarConfig();
        Mensagem(mensagem, erro);
    }

    // =================================================================
    // Teclado e ciclo de vida
    // =================================================================
    protected override bool ProcessCmdKey(ref Message msg, Keys k)
    {
        // Editando um texto salvo: so Esc e Ctrl+S sao especiais
        if (editando && editor.Focused)
        {
            if (k == Keys.Escape) { CancelarEdicao(); return true; }
            if (k == (Keys.Control | Keys.S) || k == (Keys.Control | Keys.Enter)) { SalvarEdicao(); return true; }
            return base.ProcessCmdKey(ref msg, k);
        }

        if (k == Keys.Escape) { Fechar(); return true; }
        if (k == (Keys.Control | Keys.Tab))              { MostrarPagina((Math.Min(pagina, 2) + 1) % 3); return true; }
        if (k == (Keys.Control | Keys.Shift | Keys.Tab)) { MostrarPagina((Math.Min(pagina, 2) + 2) % 3); return true; }

        if (pagina == PgHistorico || pagina == PgSalvos)
        {
            ListaCartoes l = pagina == PgHistorico ? listaHist : listaSalvos;
            if (pagina == PgSalvos && editando && k == (Keys.Control | Keys.V) &&
                Clipboard.ContainsImage() && !Clipboard.ContainsText())
            {
                ColarImagemSalvo();
                return true;
            }
            AplicarBuscaPendente(k);
            switch (k)
            {
                case Keys.Control | Keys.S:
                    if (pagina == PgHistorico) SalvarImagemComo();
                    return true;
                case Keys.Control | Keys.E:
                    if (pagina == PgHistorico) EditarImagem();
                    return true;
                case Keys.Enter:
                    if (pagina == PgHistorico) { Item it = ItemHistAtual(); if (it != null) Escolher(it); }
                    else { Salvo s = SalvoAtual(); if (s != null) Escolher(Envio(s)); }
                    return true;
                case Keys.Down:
                    if (l.SelectedIndex < l.Items.Count - 1) l.SelectedIndex++;
                    return true;
                case Keys.Up:
                    if (l.SelectedIndex > 0) l.SelectedIndex--;
                    return true;
                case Keys.Shift | Keys.Delete:
                    if (pagina == PgHistorico) ApagarHistorico(); else ApagarSalvo();
                    return true;
                case Keys.F2:
                    if (pagina == PgSalvos) { Salvo s = SalvoAtual(); if (s != null) ComecarEdicao(s); }
                    return true;
            }
            if (k == Keys.Delete && l.Focused)
            {
                if (pagina == PgHistorico) ApagarHistorico(); else ApagarSalvo();
                return true;
            }
        }
        else if (pagina == PgEmotes)
        {
            bool textoNaBusca = busca.Focused && busca.TextLength > 0;
            AplicarBuscaPendente(k);
            switch (k)
            {
                case Keys.Enter: grade.EscolherAtual(); return true;
                case Keys.Down:  grade.Mover(0, 1); return true;
                case Keys.Up:    grade.Mover(0, -1); return true;
                case Keys.Right: if (textoNaBusca) break; grade.Mover(1, 0); return true;
                case Keys.Left:  if (textoNaBusca) break; grade.Mover(-1, 0); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, k);
    }

    // Enter ou seta logo depois de digitar: filtra antes de agir
    void AplicarBuscaPendente(Keys k)
    {
        if (!esperaBusca.Enabled) return;
        if (k == Keys.Enter || k == Keys.Down || k == Keys.Up || k == Keys.Left || k == Keys.Right)
        {
            esperaBusca.Stop();
            Filtrar();
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AtualizarDicaBusca();
#if !TESTE
        Activate();
#endif
        if (pagina != PgConfig) busca.Focus();
    }

#if TESTE
    protected override bool ShowWithoutActivation { get { return true; } }
#endif

    // Fecha ao clicar fora, como o Win + V original
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (gravando < 0 && !dialogoAberto) Fechar();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (editando) SalvarEdicao();
        PararGravacao();
        nucleo.TeclaGravada -= TeclaGravada;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (previaImagem != null && previaImagem.Image != null)
            {
                previaImagem.Image.Dispose();
                previaImagem.Image = null;
            }
            if (dicas != null) dicas.Dispose();
            if (esperaBusca != null) esperaBusca.Dispose();
            if (botoesCat != null)
                foreach (Label b in botoesCat)
                    if (b != null && b.Image != null) { b.Image.Dispose(); b.Image = null; }
            if (statusIcone != null && statusIcone.Image != null) { statusIcone.Image.Dispose(); statusIcone.Image = null; }
            EmojiCor.Liberar();
            Emotes.Liberar();
        }
        base.Dispose(disposing);
    }
}

// ---------------------------------------------------------------------
// 8b. EDITOR DE IMAGEM: abre em tela cheia a partir do historico
// ---------------------------------------------------------------------
enum Ferramenta { Mover, Lapis, Marca, Borracha, Borrar, BorrarArea, Seta, Retangulo, Texto, Recortar }

// Os pixels e o desfazer, sem nada de tela: da para testar sem abrir janela.
// Tudo e desenhado direto na imagem, entao o que foi borrado ou apagado deixa
// de existir no arquivo final, em vez de ficar escondido embaixo de uma camada.
class Quadro : IDisposable
{
    const long LimiteDesfazer = 256L * 1024 * 1024;   // processo de 32 bits: nao passa disso
    const int  LimitePassos   = 60;

    // Um passo guarda so a area que mudou. Desfazer e refazer trocam os
    // pixels guardados pelos que estao na imagem.
    class Passo
    {
        public Rectangle Area;
        public Bitmap    Pixels;
        public bool      Inteiro;   // recorte: guarda a imagem toda
        public int       Versao;
        public long Bytes { get { return (long)Pixels.Width * Pixels.Height * 4; } }
    }

    public Bitmap Imagem;
    Bitmap antes;       // ultimo estado confirmado
    Bitmap borrada;     // copia borrada da imagem, feita so quando alguem borra
    readonly List<Passo> desfazer = new List<Passo>();
    readonly List<Passo> refazer  = new List<Passo>();
    int versao, versaoSalva, contador;

    // Traco em andamento
    Ferramenta tipo;
    bool emTraco;
    Pen caneta;
    TextureBrush textura;
    PointF inicio, ultimo;
    readonly List<PointF> pontos = new List<PointF>();
    Rectangle tocado, desenhado;   // tudo o que o traco mexeu / o ultimo redesenho

    public Quadro(Image origem)
    {
        Imagem = NovaCopia(origem);
        antes  = NovaCopia(Imagem);
    }

    public int  Largura     { get { return Imagem.Width; } }
    public int  Altura      { get { return Imagem.Height; } }
    public bool Alterado    { get { return versao != versaoSalva; } }
    public bool PodeDesfazer { get { return desfazer.Count > 0; } }
    public bool PodeRefazer  { get { return refazer.Count > 0; } }
    public void MarcarSalvo() { versaoSalva = versao; }
    Rectangle Limites { get { return new Rectangle(0, 0, Imagem.Width, Imagem.Height); } }

    static Bitmap NovaCopia(Image img)
    {
        Bitmap b = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
        }
        return b;
    }

    static Bitmap Recorte(Bitmap de, Rectangle r)
    {
        Bitmap b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(de, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
        }
        return b;
    }

    static void Colar(Bitmap em, Bitmap pixels, Rectangle r)
    {
        using (Graphics g = Graphics.FromImage(em))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(pixels, r, new Rectangle(0, 0, r.Width, r.Height), GraphicsUnit.Pixel);
        }
    }

    static void CopiarArea(Bitmap de, Bitmap em, Rectangle r)
    {
        using (Graphics g = Graphics.FromImage(em))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(de, r, r, GraphicsUnit.Pixel);
        }
    }

    Graphics Pintor()
    {
        Graphics g = Graphics.FromImage(Imagem);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        return g;
    }

    // ---- Tracos ----
    public void ComecarTraco(Ferramenta f, Color cor, int espessura, PointF p)
    {
        if (emTraco) CancelarTraco();
        tipo    = f;
        inicio  = ultimo = p;
        tocado  = desenhado = Rectangle.Empty;
        emTraco = true;
        pontos.Clear();
        pontos.Add(p);

        if (f == Ferramenta.Borrar || f == Ferramenta.BorrarArea) textura = new TextureBrush(Borrada());

        int largura = f == Ferramenta.Marca ? Math.Max(10, espessura * 3) : espessura;
        if (f == Ferramenta.Marca) cor = Color.FromArgb(105, cor);
        if (f == Ferramenta.Borracha) cor = Color.Transparent;
        caneta = f == Ferramenta.Borrar ? new Pen(textura, largura) : new Pen(cor, largura);
        caneta.StartCap = caneta.EndCap = LineCap.Round;
        caneta.LineJoin = LineJoin.Round;

        if (f == Ferramenta.Seta)
        {
            caneta.StartCap = LineCap.Round;
            using (AdjustableArrowCap ponta = new AdjustableArrowCap(3.5f, 4.5f, true)) caneta.CustomEndCap = ponta;
        }

        if (f == Ferramenta.Lapis || f == Ferramenta.Borracha || f == Ferramenta.Borrar) Ponto(p);
        else MoverTraco(p);
    }

    public Rectangle MoverTraco(PointF p)
    {
        if (!emTraco) return Rectangle.Empty;
        Rectangle area;
        if (tipo == Ferramenta.Lapis || tipo == Ferramenta.Borracha || tipo == Ferramenta.Borrar)
        {
            using (Graphics g = Pintor())
            {
                Preparar(g);
                g.DrawLine(caneta, ultimo, p);
            }
            area = Caixa(ultimo, p, caneta.Width);
            Marcar(area);
        }
        else
        {
            pontos.Add(p);
            area = Redesenhar(p);
        }
        ultimo = p;
        return area;
    }

    // Marca-texto e formas sao refeitos do zero a cada movimento: o marca-texto
    // e transparente e escureceria onde o traco passa por cima dele mesmo.
    Rectangle Redesenhar(PointF p)
    {
        Rectangle anterior = desenhado;
        if (!anterior.IsEmpty) CopiarArea(antes, Imagem, anterior);

        float folga = caneta.Width;
        if (tipo == Ferramenta.Seta) folga = caneta.Width * 6;
        Rectangle nova;
        using (Graphics g = Pintor())
        {
            switch (tipo)
            {
                case Ferramenta.Marca:
                    if (pontos.Count > 1) g.DrawLines(caneta, pontos.ToArray());
                    nova = CaixaDe(pontos, folga);
                    break;
                case Ferramenta.Seta:
                    if (Distancia(inicio, p) >= 2) g.DrawLine(caneta, inicio, p);
                    nova = Caixa(inicio, p, folga);
                    break;
                case Ferramenta.Retangulo:
                    RectangleF r = Retangulo(inicio, p);
                    if (r.Width >= 1 && r.Height >= 1) g.DrawRectangle(caneta, r.X, r.Y, r.Width, r.Height);
                    nova = Caixa(inicio, p, folga);
                    break;
                default:   // BorrarArea
                    g.FillRectangle(textura, Retangulo(inicio, p));
                    nova = Caixa(inicio, p, 2);
                    break;
            }
        }
        desenhado = Rectangle.Intersect(nova, Limites);
        Marcar(desenhado);
        return anterior.IsEmpty ? desenhado : Rectangle.Union(anterior, desenhado);
    }

    // A borracha troca os pixels em vez de somar: deixa a area transparente
    void Preparar(Graphics g)
    {
        if (tipo != Ferramenta.Borracha) return;
        g.SmoothingMode = SmoothingMode.None;
        g.CompositingMode = CompositingMode.SourceCopy;
    }

    // Um clique sem arrastar tambem marca
    void Ponto(PointF p)
    {
        float d = caneta.Width;
        using (Graphics g = Pintor())
        {
            Preparar(g);
            using (Brush b = tipo == Ferramenta.Borrar ? (Brush)textura.Clone() : new SolidBrush(caneta.Color))
                g.FillEllipse(b, p.X - d / 2, p.Y - d / 2, d, d);
        }
        Marcar(Caixa(p, p, d));
    }

    void Marcar(Rectangle r)
    {
        r = Rectangle.Intersect(r, Limites);
        if (r.Width <= 0 || r.Height <= 0) return;
        tocado = tocado.IsEmpty ? r : Rectangle.Union(tocado, r);
    }

    public void TerminarTraco()
    {
        if (!emTraco) return;
        Rectangle area = tocado;
        bool vazio = tipo != Ferramenta.Lapis && tipo != Ferramenta.Borracha && tipo != Ferramenta.Borrar &&
                     tipo != Ferramenta.Marca && Distancia(inicio, ultimo) < 2;
        FecharTraco();
        if (vazio) { if (!area.IsEmpty) CopiarArea(antes, Imagem, area); return; }
        Confirmar(area);
    }

    public void CancelarTraco()
    {
        if (!emTraco) return;
        Rectangle area = tocado;
        FecharTraco();
        if (!area.IsEmpty) CopiarArea(antes, Imagem, area);
    }

    void FecharTraco()
    {
        emTraco = false;
        if (caneta != null)  { caneta.Dispose(); caneta = null; }
        if (textura != null) { textura.Dispose(); textura = null; }
        pontos.Clear();
    }

    static float Distancia(PointF a, PointF b)
    {
        float x = a.X - b.X, y = a.Y - b.Y;
        return (float)Math.Sqrt(x * x + y * y);
    }

    public static RectangleF Retangulo(PointF a, PointF b)
    {
        return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    static Rectangle Caixa(PointF a, PointF b, float folga)
    {
        RectangleF r = Retangulo(a, b);
        r.Inflate(folga / 2 + 2, folga / 2 + 2);
        return Rectangle.FromLTRB((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
    }

    static Rectangle CaixaDe(List<PointF> ps, float folga)
    {
        Rectangle r = Caixa(ps[0], ps[0], folga);
        foreach (PointF p in ps) r = Rectangle.Union(r, Caixa(p, p, folga));
        return r;
    }

    // Reduz bastante e amplia de volta. O tamanho do bloco cresce com a imagem
    // para um print 4K ficar tao ilegivel quanto um Full HD.
    Bitmap Borrada()
    {
        if (borrada != null) return borrada;
        int w = Imagem.Width, h = Imagem.Height;
        int passo = Math.Max(10, Math.Max(w, h) / 150);
        int pw = Math.Max(1, w / passo), ph = Math.Max(1, h / passo);

        using (ImageAttributes borda = new ImageAttributes())
        using (Bitmap pequena = new Bitmap(pw, ph, PixelFormat.Format32bppPArgb))
        {
            borda.SetWrapMode(WrapMode.TileFlipXY);   // sem isso as beiradas escurecem
            using (Graphics g = Graphics.FromImage(pequena))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
                g.DrawImage(Imagem, new Rectangle(0, 0, pw, ph), 0, 0, w, h, GraphicsUnit.Pixel, borda);
            }
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
                g.DrawImage(pequena, new Rectangle(0, 0, w, h), 0, 0, pw, ph, GraphicsUnit.Pixel, borda);
            }
            borrada = b;
        }
        return borrada;
    }

    void SoltarBorrada()
    {
        if (borrada != null) { borrada.Dispose(); borrada = null; }
    }

    // ---- Texto, imagem por cima e recorte ----
    public void Escrever(string texto, Font fonte, Color cor, PointF onde)
    {
        if (string.IsNullOrEmpty(texto)) return;
        using (Graphics g = Pintor())
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            SizeF t = g.MeasureString(texto, fonte);
            g.DrawString(texto, fonte, Tema.Pincel(cor), onde);
            Confirmar(Caixa(onde, new PointF(onde.X + t.Width, onde.Y + t.Height), 4));
        }
    }

    public void Estampar(Image img, RectangleF onde)
    {
        using (Graphics g = Pintor())
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, onde);
        }
        Confirmar(Caixa(onde.Location, new PointF(onde.Right, onde.Bottom), 2));
    }

    public bool Recortar(Rectangle r)
    {
        r = Rectangle.Intersect(r, Limites);
        if (r.Width < 2 || r.Height < 2) return false;

        Passo p = new Passo();
        p.Inteiro = true;
        p.Pixels  = Imagem;
        p.Versao  = versao;
        Imagem = Recorte(p.Pixels, r);
        antes.Dispose();
        antes = NovaCopia(Imagem);
        Registrar(p);
        return true;
    }

    // ---- Desfazer ----
    void Confirmar(Rectangle area)
    {
        area = Rectangle.Intersect(area, Limites);
        if (area.Width <= 0 || area.Height <= 0) return;

        Passo p = new Passo();
        p.Area   = area;
        p.Pixels = Recorte(antes, area);
        p.Versao = versao;
        CopiarArea(Imagem, antes, area);
        Registrar(p);
    }

    void Registrar(Passo p)
    {
        desfazer.Add(p);
        Soltar(refazer);
        versao = ++contador;
        SoltarBorrada();

        long total = 0;
        foreach (Passo d in desfazer) total += d.Bytes;
        while (desfazer.Count > 1 && (desfazer.Count > LimitePassos || total > LimiteDesfazer))
        {
            total -= desfazer[0].Bytes;
            desfazer[0].Pixels.Dispose();
            desfazer.RemoveAt(0);
        }
    }

    static void Soltar(List<Passo> lista)
    {
        foreach (Passo p in lista) p.Pixels.Dispose();
        lista.Clear();
    }

    public bool Desfazer() { return Trocar(desfazer, refazer); }
    public bool Refazer()  { return Trocar(refazer, desfazer); }

    bool Trocar(List<Passo> de, List<Passo> para)
    {
        if (de.Count == 0) return false;
        Passo p = de[de.Count - 1];
        de.RemoveAt(de.Count - 1);

        if (p.Inteiro)
        {
            Bitmap atual = Imagem;
            Imagem   = p.Pixels;
            p.Pixels = atual;
            antes.Dispose();
            antes = NovaCopia(Imagem);
        }
        else
        {
            Bitmap atual = Recorte(Imagem, p.Area);
            Colar(Imagem, p.Pixels, p.Area);
            Colar(antes, p.Pixels, p.Area);
            p.Pixels.Dispose();
            p.Pixels = atual;
        }

        int v = versao;
        versao   = p.Versao;
        p.Versao = v;
        para.Add(p);
        SoltarBorrada();
        return true;
    }

    public void Dispose()
    {
        FecharTraco();
        Soltar(desfazer);
        Soltar(refazer);
        SoltarBorrada();
        if (antes != null)  { antes.Dispose(); antes = null; }
        if (Imagem != null) { Imagem.Dispose(); Imagem = null; }
    }
}

// Superficie de desenho, sem piscar
class TelaEditor : Control
{
    public TelaEditor()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(14, 14, 18);
    }

    // Setas e Tab ficam para o editor, em vez de trocar de controle
    protected override bool IsInputKey(Keys k) { return true; }
}

class EditorImagem : Form
{
    static readonly Color[] Cores =
    {
        Color.FromArgb(239, 68, 68), Color.FromArgb(250, 204, 21), Color.FromArgb(34, 197, 94),
        Color.FromArgb(59, 130, 246), Color.FromArgb(139, 92, 246), Color.FromArgb(236, 72, 153),
        Color.White, Color.Black
    };
    static readonly int[] Tamanhos = { 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96 };

    // Ficam guardados entre uma edicao e outra
    static Color cor = Cores[0];
    static int indiceTamanho = 3;

    readonly Quadro quadro;
    readonly TelaEditor tela;
    readonly ToolTip dicas = new ToolTip();
    readonly Label rotuloTamanho, rotuloZoom, rotuloDica, rotuloAviso;
    readonly Dictionary<Ferramenta, Label> botoes = new Dictionary<Ferramenta, Label>();
    readonly List<Label> botoesCor = new List<Label>();
    readonly Label botaoDesfazer, botaoRefazer, botaoImagem;
    readonly TextureBrush xadrez;
    readonly Icon icone;
    readonly Timer apagaAviso = new Timer();

    Ferramenta ferramenta = Ferramenta.Lapis;
    bool saindo;

    // Vista: ponto da imagem (x, y) aparece na tela em (x * zoom + ox, y * zoom + oy)
    float zoom = 1, ox, oy;
    bool ajustada = true;

    // Mouse
    bool desenhando, recortando, arrastando, espaco;
    Point arrasteMouse;
    PointF arrasteVista;
    PointF cantoRecorte, fimRecorte;
    Point mouse = new Point(-100, -100);

    // Imagem colada, ainda solta: da para mover e redimensionar
    Bitmap flutuante;
    RectangleF posFlutuante, posInicial;
    int alca = -1;
    PointF inicioFlutuante;
    bool movendoFlutuante;

    // Texto sendo digitado
    TextBox caixaTexto;
    PointF posTexto;

    public EditorImagem(Bitmap imagem)
    {
        quadro = new Quadro(imagem);
        imagem.Dispose();

        icone = Tema.CriarIcone();
        Icon = icone;
        Text = "Editor de imagem";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        BackColor       = Tema.Fundo;
        KeyPreview      = true;
        Bounds          = Screen.FromPoint(Cursor.Position).Bounds;

        using (Bitmap b = new Bitmap(16, 16))
        {
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(58, 58, 66));
                g.FillRectangle(Tema.Pincel(Color.FromArgb(44, 44, 50)), 0, 0, 8, 8);
                g.FillRectangle(Tema.Pincel(Color.FromArgb(44, 44, 50)), 8, 8, 8, 8);
            }
            xadrez = new TextureBrush(b);
        }

        tela = new TelaEditor();
        tela.Dock = DockStyle.Fill;
        tela.Paint      += Pintar;
        tela.MouseDown  += MouseApertou;
        tela.MouseMove  += MouseMoveu;
        tela.MouseUp    += MouseSoltou;
        tela.MouseWheel += Roda;
        tela.MouseLeave += delegate { Point velho = mouse; mouse = new Point(-100, -100); InvalidarPincel(velho); };
        tela.Resize     += delegate { if (ajustada) Ajustar(); };

        // ---- Primeira linha: ferramentas e acoes ----
        Panel linha1 = new Panel();
        linha1.Dock = DockStyle.Top;
        linha1.Height = 46;
        linha1.Width = Width;   // os botoes ancorados a direita medem a partir daqui
        linha1.BackColor = Tema.Fundo;

        int x = 10;
        x = BotaoFerramenta(linha1, Ferramenta.Mover,      "Mover",       "Mover a vis\u00e3o (V). Segure Espa\u00e7o em qualquer ferramenta", 62, x);
        x = BotaoFerramenta(linha1, Ferramenta.Lapis,      "L\u00e1pis",  "Rabiscar (L)", 58, x);
        x = BotaoFerramenta(linha1, Ferramenta.Marca,      "Marca-texto", "Grifar por cima (M)", 92, x);
        x = BotaoFerramenta(linha1, Ferramenta.Borracha,   "Borracha",    "Apagar parte da imagem (E)", 76, x);
        x = BotaoFerramenta(linha1, Ferramenta.Borrar,     "Borrar",      "Passar o pincel para borrar (B)", 62, x);
        x = BotaoFerramenta(linha1, Ferramenta.BorrarArea, "Borrar \u00e1rea", "Arrastar um ret\u00e2ngulo para borrar (A)", 92, x);
        x = BotaoFerramenta(linha1, Ferramenta.Seta,       "Seta",        "Desenhar seta (S)", 52, x);
        x = BotaoFerramenta(linha1, Ferramenta.Retangulo,  "Ret\u00e2ngulo", "Desenhar ret\u00e2ngulo (R)", 82, x);
        x = BotaoFerramenta(linha1, Ferramenta.Texto,      "Texto",       "Escrever na imagem (T)", 56, x);
        x = BotaoFerramenta(linha1, Ferramenta.Recortar,   "Recortar",    "Arrastar para recortar (C)", 72, x);

        botaoImagem = Botao("Imagem por cima", 124, "Colocar outra imagem por cima (I). Ctrl+V cola da \u00e1rea de transfer\u00eancia");
        botaoImagem.Location = new Point(x + 8, 7);
        botaoImagem.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MenuImagem(); };
        linha1.Controls.Add(botaoImagem);

        Label fechar = BotaoDireita(linha1, "Fechar", 66, "Fechar o editor (Esc)");
        fechar.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Close(); };

        Label salvar = BotaoDireita(linha1, "Salvar como...", 104, "Salvar em arquivo (Ctrl+S)");
        salvar.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) SalvarComo(); };

        Label copiar = BotaoDireita(linha1, "Copiar", 72, "Copiar o resultado e guardar no hist\u00f3rico (Ctrl+C)");
        copiar.BackColor = Tema.Destaque;
        copiar.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Copiar(); };

        // ---- Segunda linha: cor, tamanho e avisos ----
        Panel linha2 = new Panel();
        linha2.Dock = DockStyle.Top;
        linha2.Height = 38;
        linha2.Width = Width;
        linha2.BackColor = Tema.Painel;

        x = 12;
        foreach (Color c in Cores)
        {
            Label l = new Label();
            l.Location = new Point(x, 8);
            l.Size = new Size(22, 22);
            l.BackColor = c;
            l.Cursor = Cursors.Hand;
            Color esta = c;
            l.MouseDown += delegate { EscolherCor(esta); };
            l.Paint += delegate(object s, PaintEventArgs e)
            {
                if (l.BackColor.ToArgb() != cor.ToArgb()) return;
                Color contraste = l.BackColor.GetBrightness() > 0.5f ? Color.Black : Color.White;
                using (Pen p = new Pen(contraste, 2)) e.Graphics.DrawRectangle(p, 2, 2, 17, 17);
            };
            botoesCor.Add(l);
            linha2.Controls.Add(l);
            x += 26;
        }

        Label outra = Botao("Outra...", 62, "Escolher outra cor");
        outra.Location = new Point(x + 4, 5);
        outra.Height = 28;
        outra.Font = Tema.Pequena;
        outra.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OutraCor(); };
        linha2.Controls.Add(outra);
        x += 80;

        Label menos = Botao("\u2212", 28, "Tra\u00e7o mais fino ([)");
        menos.Location = new Point(x, 5);
        menos.Height = 28;
        menos.MouseDown += delegate { MudarTamanho(-1); };
        rotuloTamanho = NovoRotulo("", Tema.Normal, Tema.Texto, HorizontalAlignment.Center);
        rotuloTamanho.Location = new Point(x + 28, 5);
        rotuloTamanho.Size = new Size(58, 28);
        Label mais = Botao("+", 28, "Tra\u00e7o mais grosso (])");
        mais.Location = new Point(x + 86, 5);
        mais.Height = 28;
        mais.MouseDown += delegate { MudarTamanho(1); };
        linha2.Controls.AddRange(new Control[] { menos, rotuloTamanho, mais });
        x += 128;

        botaoDesfazer = Botao("Desfazer", 74, "Desfazer (Ctrl+Z)");
        botaoDesfazer.Location = new Point(x, 5);
        botaoDesfazer.Height = 28;
        botaoDesfazer.Font = Tema.Pequena;
        botaoDesfazer.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Desfazer(); };
        botaoRefazer = Botao("Refazer", 68, "Refazer (Ctrl+Y)");
        botaoRefazer.Location = new Point(x + 78, 5);
        botaoRefazer.Height = 28;
        botaoRefazer.Font = Tema.Pequena;
        botaoRefazer.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Refazer(); };
        linha2.Controls.AddRange(new Control[] { botaoDesfazer, botaoRefazer });
        x += 158;

        rotuloZoom = NovoRotulo("", Tema.Pequena, Tema.Suave, HorizontalAlignment.Right);
        rotuloZoom.Dock = DockStyle.Right;
        rotuloZoom.Width = 170;
        rotuloZoom.Cursor = Cursors.Hand;
        rotuloZoom.MouseDown += delegate { Ajustar(); };
        dicas.SetToolTip(rotuloZoom, "Clique para caber na tela (Ctrl+0)");

        rotuloDica = NovoRotulo("", Tema.Pequena, Tema.Suave, HorizontalAlignment.Left);
        rotuloDica.Location = new Point(x + 6, 5);
        rotuloDica.Size = new Size(Math.Max(100, Width - x - 190), 28);
        rotuloDica.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

        rotuloAviso = rotuloDica;
        linha2.Controls.Add(rotuloDica);
        linha2.Controls.Add(rotuloZoom);

        Controls.Add(tela);
        Controls.Add(linha2);
        Controls.Add(linha1);

        apagaAviso.Interval = 4000;
        apagaAviso.Tick += delegate { apagaAviso.Stop(); MostrarDica(); };

        AtualizarTamanho();
        TrocarFerramenta(Ferramenta.Lapis);
        AtualizarBotoes();
    }

    // ---- Montagem dos controles ----
    static Label NovoRotulo(string texto, Font fonte, Color corTexto, HorizontalAlignment lado)
    {
        Label l = new Label();
        l.Text = texto;
        l.Font = fonte;
        l.ForeColor = corTexto;
        l.BackColor = Color.Transparent;
        l.UseMnemonic = false;
        l.TextAlign = lado == HorizontalAlignment.Center ? ContentAlignment.MiddleCenter :
                      lado == HorizontalAlignment.Right ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
        return l;
    }

    Label Botao(string texto, int largura, string dica)
    {
        Label l = NovoRotulo(texto, Tema.Normal, Tema.Texto, HorizontalAlignment.Center);
        l.Size = new Size(largura, 32);
        l.BackColor = Tema.Campo;
        l.Cursor = Cursors.Hand;
        l.MouseEnter += delegate { if (l.BackColor == Tema.Campo) l.BackColor = Tema.CampoSob; };
        l.MouseLeave += delegate { if (l.BackColor == Tema.CampoSob) l.BackColor = Tema.Campo; };
        dicas.SetToolTip(l, dica);
        return l;
    }

    int BotaoFerramenta(Panel p, Ferramenta f, string texto, string dica, int largura, int x)
    {
        Label l = Botao(texto, largura, dica);
        l.Location = new Point(x, 7);
        l.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) TrocarFerramenta(f); };
        botoes[f] = l;
        p.Controls.Add(l);
        return x + largura + 4;
    }

    Label BotaoDireita(Panel p, string texto, int largura, string dica)
    {
        Label l = Botao(texto, largura, dica);
        int direita = p.Tag == null ? Width - 10 : (int)p.Tag;
        l.Location = new Point(direita - largura, 7);
        l.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        p.Tag = direita - largura - 6;
        p.Controls.Add(l);
        return l;
    }

    // ---- Estado ----
    bool UsaTamanho { get { return ferramenta == Ferramenta.Lapis || ferramenta == Ferramenta.Marca || ferramenta == Ferramenta.Borracha || ferramenta == Ferramenta.Borrar; } }

    public void TrocarFerramenta(Ferramenta f)
    {
        Assentar();
        ferramenta = f;
        foreach (KeyValuePair<Ferramenta, Label> b in botoes)
            b.Value.BackColor = b.Key == f ? Tema.Selecao : Tema.Campo;
        Point velho = mouse;
        InvalidarPincel(velho);
        tela.Cursor = CursorDaFerramenta();
        apagaAviso.Stop();
        MostrarDica();
    }

    Cursor CursorDaFerramenta()
    {
        if (ferramenta == Ferramenta.Mover || espaco) return Cursors.Hand;
        if (ferramenta == Ferramenta.Texto) return Cursors.IBeam;
        return Cursors.Cross;
    }

    void MostrarDica()
    {
        string d;
        switch (ferramenta)
        {
            case Ferramenta.Mover:      d = "Arraste para mover a imagem. Ctrl + roda aproxima, Shift + roda anda para os lados."; break;
            case Ferramenta.Lapis:      d = "Arraste para rabiscar."; break;
            case Ferramenta.Marca:      d = "Arraste para grifar."; break;
            case Ferramenta.Borracha:   d = "Arraste para apagar: o que sair fica transparente."; break;
            case Ferramenta.Borrar:     d = "Arraste por cima do que quer esconder."; break;
            case Ferramenta.BorrarArea: d = "Arraste um ret\u00e2ngulo por cima do que quer esconder."; break;
            case Ferramenta.Seta:       d = "Arraste do come\u00e7o at\u00e9 a ponta da seta."; break;
            case Ferramenta.Retangulo:  d = "Arraste de um canto ao outro."; break;
            case Ferramenta.Texto:      d = "Clique onde quer escrever. Enter confirma, Shift+Enter pula linha."; break;
            default:                    d = "Arraste sobre a parte que quer manter. Ctrl+Z volta atr\u00e1s."; break;
        }
        rotuloDica.ForeColor = Tema.Suave;
        rotuloDica.Text = d;
    }

    void Avisar(string texto, bool erro)
    {
        rotuloAviso.ForeColor = erro ? Tema.Perigo : Tema.Sucesso;
        rotuloAviso.Text = texto;
        apagaAviso.Stop();
        apagaAviso.Start();
    }

    void AtualizarBotoes()
    {
        botaoDesfazer.ForeColor = quadro.PodeDesfazer ? Tema.Texto : Tema.Suave;
        botaoRefazer.ForeColor  = quadro.PodeRefazer  ? Tema.Texto : Tema.Suave;
        rotuloZoom.Text = quadro.Largura + " x " + quadro.Altura + Tema.Ponto + (int)Math.Round(zoom * 100) + "%  ";
    }

    void EscolherCor(Color c)
    {
        cor = c;
        foreach (Label l in botoesCor) l.Invalidate();
        if (caixaTexto != null) caixaTexto.ForeColor = c;
    }

    void OutraCor()
    {
        using (ColorDialog d = new ColorDialog())
        {
            d.Color = cor;
            d.FullOpen = true;
            if (d.ShowDialog(this) == DialogResult.OK) EscolherCor(d.Color);
        }
    }

    void MudarTamanho(int passo)
    {
        Point velho = mouse;
        indiceTamanho = Math.Max(0, Math.Min(Tamanhos.Length - 1, indiceTamanho + passo));
        AtualizarTamanho();
        InvalidarPincel(velho);
    }

    void AtualizarTamanho() { rotuloTamanho.Text = Tamanhos[indiceTamanho] + " px"; }

    // ---- Vista ----
    RectangleF ImagemNaTela { get { return new RectangleF(ox, oy, quadro.Largura * zoom, quadro.Altura * zoom); } }

    PointF NaImagem(Point p) { return new PointF((p.X - ox) / zoom, (p.Y - oy) / zoom); }

    RectangleF NaTela(RectangleF r)
    {
        return new RectangleF(r.X * zoom + ox, r.Y * zoom + oy, r.Width * zoom, r.Height * zoom);
    }

    public void Ajustar()
    {
        if (tela.ClientSize.Width <= 0 || tela.ClientSize.Height <= 0) return;
        const int margem = 16;
        float z = Math.Min((tela.ClientSize.Width - margem * 2f) / quadro.Largura, (tela.ClientSize.Height - margem * 2f) / quadro.Altura);
        zoom = Math.Max(0.02f, Math.Min(1f, z));   // imagem pequena fica no tamanho real, sem borrar
        ox = (float)Math.Round((tela.ClientSize.Width - quadro.Largura * zoom) / 2);
        oy = (float)Math.Round((tela.ClientSize.Height - quadro.Altura * zoom) / 2);
        ajustada = true;
        AtualizarBotoes();
        tela.Invalidate();
    }

    void Roda(object s, MouseEventArgs e) { Rolar(e.Delta, ModifierKeys, e.Location); }

    // Como no Canva: Ctrl aproxima e afasta, Shift anda para os lados, sozinha
    // anda para cima e para baixo. A imagem solta nao e fixada: ela acompanha.
    public void Rolar(int delta, Keys mods, Point p)
    {
        if (desenhando || recortando || delta == 0) return;
        FecharTexto(true);

        if ((mods & Keys.Control) != 0)
        {
            float novo = Math.Max(0.05f, Math.Min(16f, zoom * (delta > 0 ? 1.25f : 0.8f)));
            // Quase 100%: encaixa, para os pixels nao ficarem interpolados
            if (Math.Abs(novo - 1f) < 0.08f) novo = 1f;
            PointF ponto = NaImagem(p);
            zoom = novo;
            ox = (float)Math.Round(p.X - ponto.X * zoom);
            oy = (float)Math.Round(p.Y - ponto.Y * zoom);
        }
        else
        {
            float passo = delta / 2f;   // 60 pixels por dente da roda
            if ((mods & Keys.Shift) != 0) ox = Deslizar(ox, quadro.Largura * zoom, tela.ClientSize.Width, passo);
            else                          oy = Deslizar(oy, quadro.Altura * zoom, tela.ClientSize.Height, passo);
        }
        ajustada = false;
        AtualizarBotoes();
        tela.Invalidate();
    }

    // Imagem que cabe nao anda; a que nao cabe para na beirada, sem sumir da tela
    static float Deslizar(float posicao, float tamanho, float espaco, float passo)
    {
        const float folga = 16;
        if (tamanho + folga * 2 <= espaco) return posicao;
        return Math.Max(espaco - tamanho - folga, Math.Min(folga, posicao + passo));
    }

    void InvalidarImagem(Rectangle r)
    {
        if (r.IsEmpty) return;
        RectangleF t = NaTela(r);
        t.Inflate(3, 3);
        tela.Invalidate(Rectangle.Ceiling(t));
    }

    void InvalidarPincel(Point p)
    {
        if (!UsaTamanho) return;
        int r = (int)Math.Ceiling(RaioPincel() * zoom) + 4;
        tela.Invalidate(new Rectangle(p.X - r, p.Y - r, r * 2, r * 2));
    }

    float RaioPincel()
    {
        int t = Tamanhos[indiceTamanho];
        if (ferramenta == Ferramenta.Marca) t = Math.Max(10, t * 3);
        return t / 2f;
    }

    // ---- Desenho na tela ----
    void Pintar(object s, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(tela.BackColor);

        RectangleF area = ImagemNaTela;
        RectangleF visivel = RectangleF.Intersect(area, e.ClipRectangle);
        if (visivel.Width > 0 && visivel.Height > 0) g.FillRectangle(xadrez, visivel);

        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.InterpolationMode = zoom >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
        g.DrawImage(quadro.Imagem, area, new RectangleF(0, 0, quadro.Largura, quadro.Altura), GraphicsUnit.Pixel);
        g.PixelOffsetMode = PixelOffsetMode.Default;

        if (flutuante != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            RectangleF r = NaTela(posFlutuante);
            g.DrawImage(flutuante, r);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            using (Pen p = new Pen(Tema.Destaque, 2))
            {
                p.DashStyle = DashStyle.Dash;
                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            }
            foreach (RectangleF a in Alcas()) { g.FillRectangle(Brushes.White, a); g.DrawRectangle(Pens.Black, a.X, a.Y, a.Width, a.Height); }
        }

        if (recortando) PintarRecorte(g, area);

        if (UsaTamanho && !espaco && flutuante == null && caixaTexto == null && mouse.X >= 0 && !arrastando)
        {
            float raio = RaioPincel() * zoom;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawEllipse(Pens.Black, mouse.X - raio - 1, mouse.Y - raio - 1, raio * 2 + 2, raio * 2 + 2);
            g.DrawEllipse(Pens.White, mouse.X - raio, mouse.Y - raio, raio * 2, raio * 2);
            g.SmoothingMode = SmoothingMode.Default;
        }
    }

    void PintarRecorte(Graphics g, RectangleF area)
    {
        RectangleF r = NaTela(Quadro.Retangulo(cantoRecorte, fimRecorte));
        using (Region fora = new Region(area))
        using (Brush escuro = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        using (Pen p = new Pen(Color.White, 1))
        {
            fora.Exclude(r);
            g.FillRegion(escuro, fora);
            p.DashStyle = DashStyle.Dash;
            g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
        }
    }

    // ---- Mouse ----
    void MouseApertou(object s, MouseEventArgs e)
    {
        tela.Focus();
        bool mover = e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && (espaco || ferramenta == Ferramenta.Mover));
        if (mover && flutuante == null)
        {
            ComecarArraste(e.Location);
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        if (flutuante != null) { ApertouFlutuante(e.Location); return; }

        if (ferramenta == Ferramenta.Texto)
        {
            if (caixaTexto != null) Assentar();
            else AbrirTexto(e.Location);
            return;
        }

        if (ferramenta == Ferramenta.Recortar)
        {
            recortando = true;
            cantoRecorte = fimRecorte = Limitado(NaImagem(e.Location));
            return;
        }

        Assentar();
        if (ferramenta == Ferramenta.Borrar || ferramenta == Ferramenta.BorrarArea) tela.Cursor = Cursors.WaitCursor;
        quadro.ComecarTraco(ferramenta, cor, Tamanhos[indiceTamanho], NaImagem(e.Location));
        tela.Cursor = CursorDaFerramenta();
        desenhando = true;
        tela.Invalidate();
    }

    void ComecarArraste(Point p)
    {
        arrastando = true;
        arrasteMouse = p;
        arrasteVista = new PointF(ox, oy);
    }

    void MouseMoveu(object s, MouseEventArgs e)
    {
        Point velho = mouse;
        mouse = e.Location;

        if (arrastando)
        {
            ox = arrasteVista.X + e.X - arrasteMouse.X;
            oy = arrasteVista.Y + e.Y - arrasteMouse.Y;
            ajustada = false;
            tela.Invalidate();
            return;
        }

        if (desenhando)
        {
            InvalidarImagem(quadro.MoverTraco(NaImagem(e.Location)));
        }
        else if (recortando)
        {
            fimRecorte = Limitado(NaImagem(e.Location));
            tela.Invalidate();
        }
        else if (flutuante != null)
        {
            MoveuFlutuante(e.Location);
        }

        InvalidarPincel(velho);
        InvalidarPincel(mouse);
    }

    void MouseSoltou(object s, MouseEventArgs e)
    {
        if (arrastando) { arrastando = false; tela.Invalidate(); return; }

        if (desenhando)
        {
            desenhando = false;
            quadro.TerminarTraco();
            AtualizarBotoes();
            tela.Invalidate();
        }
        else if (recortando)
        {
            recortando = false;
            RectangleF r = Quadro.Retangulo(cantoRecorte, fimRecorte);
            if (quadro.Recortar(Rectangle.Round(r))) Ajustar();
            tela.Invalidate();
            AtualizarBotoes();
        }
        alca = -1;
        movendoFlutuante = false;
    }

    PointF Limitado(PointF p)
    {
        return new PointF(Math.Max(0, Math.Min(quadro.Largura, p.X)), Math.Max(0, Math.Min(quadro.Altura, p.Y)));
    }

    // ---- Imagem por cima ----
    RectangleF[] Alcas()
    {
        RectangleF r = NaTela(posFlutuante);
        const float t = 10;
        return new[]
        {
            new RectangleF(r.Left - t / 2, r.Top - t / 2, t, t),
            new RectangleF(r.Right - t / 2, r.Top - t / 2, t, t),
            new RectangleF(r.Right - t / 2, r.Bottom - t / 2, t, t),
            new RectangleF(r.Left - t / 2, r.Bottom - t / 2, t, t)
        };
    }

    int AlcaEm(Point p)
    {
        RectangleF[] a = Alcas();
        for (int i = 0; i < a.Length; i++)
        {
            RectangleF r = a[i];
            r.Inflate(5, 5);
            if (r.Contains(p)) return i;
        }
        return -1;
    }

    void ApertouFlutuante(Point p)
    {
        alca = AlcaEm(p);
        inicioFlutuante = NaImagem(p);
        posInicial = posFlutuante;
        if (alca >= 0) return;
        if (NaTela(posFlutuante).Contains(p)) { movendoFlutuante = true; return; }
        Assentar();   // clicou fora: fixa onde esta
    }

    void MoveuFlutuante(Point p)
    {
        PointF agora = NaImagem(p);
        if (movendoFlutuante)
        {
            posFlutuante.X = posInicial.X + agora.X - inicioFlutuante.X;
            posFlutuante.Y = posInicial.Y + agora.Y - inicioFlutuante.Y;
            tela.Invalidate();
        }
        else if (alca >= 0)
        {
            // O canto oposto fica parado e a proporcao se mantem
            bool direita = alca == 1 || alca == 2, baixo = alca == 2 || alca == 3;
            float fixoX = direita ? posInicial.Left : posInicial.Right;
            float fixoY = baixo ? posInicial.Top : posInicial.Bottom;
            float esc = Math.Max(Math.Abs(agora.X - fixoX) / posInicial.Width, Math.Abs(agora.Y - fixoY) / posInicial.Height);
            float w = Math.Max(4, posInicial.Width * esc), h = Math.Max(4, posInicial.Height * esc);
            posFlutuante = new RectangleF(direita ? fixoX : fixoX - w, baixo ? fixoY : fixoY - h, w, h);
            tela.Invalidate();
        }
        else
        {
            int a = AlcaEm(p);
            tela.Cursor = a == 0 || a == 2 ? Cursors.SizeNWSE : a == 1 || a == 3 ? Cursors.SizeNESW :
                          NaTela(posFlutuante).Contains(p) ? Cursors.SizeAll : Cursors.Default;
        }
    }

    public void ColocarImagem(Bitmap img)
    {
        Assentar();
        flutuante = img;
        float w = img.Width, h = img.Height;
        // Maior que a imagem de fundo: entra reduzida, para as alcas ficarem a vista
        float esc = Math.Min(1f, Math.Min(quadro.Largura * 0.6f / w, quadro.Altura * 0.6f / h));
        w *= esc; h *= esc;
        posFlutuante = new RectangleF((quadro.Largura - w) / 2, (quadro.Altura - h) / 2, w, h);
        rotuloDica.ForeColor = Tema.Suave;
        rotuloDica.Text = "Arraste para mover, puxe os cantos para mudar o tamanho. Enter fixa, Delete tira.";
        tela.Invalidate();
    }

    void DescartarFlutuante()
    {
        if (flutuante == null) return;
        flutuante.Dispose();
        flutuante = null;
        tela.Cursor = CursorDaFerramenta();
        MostrarDica();
        tela.Invalidate();
    }

    void MenuImagem()
    {
        ContextMenuStrip m = CoresMenu.NovoMenu();
        ToolStripMenuItem arquivo = new ToolStripMenuItem("Escolher arquivo...");
        arquivo.Click += delegate { ImagemDeArquivo(); };
        ToolStripMenuItem colar = new ToolStripMenuItem("Colar da \u00e1rea de transfer\u00eancia (Ctrl+V)");
        colar.Click += delegate { Colar(); };
        colar.Enabled = Clipboard.ContainsImage();
        m.Items.Add(arquivo);
        m.Items.Add(colar);

        bool primeira = true;
        foreach (Item it in Historico.Itens)
        {
            if (!it.Imagem) continue;
            if (primeira) { m.Items.Add(new ToolStripSeparator()); primeira = false; }
            ToolStripMenuItem i = new ToolStripMenuItem(Tema.Hora(it.Hora) + Tema.Ponto + it.Info);
            i.Image = it.Mini;
            i.ImageScaling = ToolStripItemImageScaling.None;
            string caminho = it.Caminho;
            i.Click += delegate { ImagemDe(caminho); };
            m.Items.Add(i);
        }
        m.Closed += delegate { BeginInvoke(new MethodInvoker(m.Dispose)); };
        m.Show(botaoImagem, new Point(0, botaoImagem.Height));
    }

    void ImagemDe(string caminho)
    {
        try { ColocarImagem(Historico.LerImagem(caminho)); }
        catch (Exception ex)
        {
            App.Log("Erro ao abrir imagem no editor: " + ex.Message);
            Avisar("N\u00e3o consegui abrir essa imagem.", true);
        }
    }

    void ImagemDeArquivo()
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "Escolher imagem";
            d.Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Todos os arquivos|*.*";
            if (d.ShowDialog(this) == DialogResult.OK) ImagemDe(d.FileName);
        }
    }

    void Colar()
    {
        try
        {
            if (!Clipboard.ContainsImage()) { Avisar("N\u00e3o tem imagem na \u00e1rea de transfer\u00eancia.", true); return; }
            Image img = Clipboard.GetImage();
            if (img != null) ColocarImagem(img as Bitmap ?? new Bitmap(img));
        }
        catch (Exception ex)
        {
            App.Log("Erro ao colar no editor: " + ex.Message);
            Avisar("N\u00e3o consegui colar.", true);
        }
    }

    // ---- Texto ----
    int TamanhoDaFonte { get { return Math.Max(16, Tamanhos[indiceTamanho] * 4); } }

    void AbrirTexto(Point p)
    {
        posTexto = NaImagem(p);
        caixaTexto = new TextBox();
        caixaTexto.Multiline = true;
        caixaTexto.AcceptsReturn = false;
        caixaTexto.BorderStyle = BorderStyle.None;
        caixaTexto.Font = new Font("Segoe UI Semibold", TamanhoDaFonte * zoom, GraphicsUnit.Pixel);
        caixaTexto.ForeColor = cor;
        caixaTexto.BackColor = cor.GetBrightness() > 0.5f ? Color.FromArgb(30, 30, 37) : Color.FromArgb(235, 235, 240);
        caixaTexto.Location = p;
        caixaTexto.TextChanged += delegate { MedirTexto(); };
        tela.Controls.Add(caixaTexto);
        MedirTexto();
        caixaTexto.Focus();
    }

    void MedirTexto()
    {
        Size t = TextRenderer.MeasureText(caixaTexto.Text.Length == 0 ? "Texto" : caixaTexto.Text + " ", caixaTexto.Font);
        caixaTexto.Size = new Size(t.Width + 12, t.Height + 6);
    }

    void FecharTexto(bool gravar)
    {
        if (caixaTexto == null) return;
        TextBox c = caixaTexto;
        caixaTexto = null;
        if (gravar && c.Text.Trim().Length > 0)
            using (Font f = new Font("Segoe UI Semibold", TamanhoDaFonte, GraphicsUnit.Pixel))
                quadro.Escrever(c.Text, f, c.ForeColor, posTexto);
        Font velha = c.Font;
        tela.Controls.Remove(c);
        c.Dispose();
        velha.Dispose();
        tela.Focus();
        AtualizarBotoes();
        tela.Invalidate();
    }

    // Fixa na imagem o que ainda esta solto (imagem por cima ou texto)
    void Assentar()
    {
        FecharTexto(true);
        if (flutuante == null) return;
        quadro.Estampar(flutuante, posFlutuante);
        DescartarFlutuante();
        AtualizarBotoes();
    }

    // ---- Acoes ----
    public void Desfazer()
    {
        if (caixaTexto != null) { FecharTexto(false); return; }
        if (flutuante != null) { DescartarFlutuante(); return; }
        if (desenhando) return;
        if (quadro.Desfazer()) DepoisDeTrocar();
    }

    public void Refazer()
    {
        if (desenhando || caixaTexto != null || flutuante != null) return;
        if (quadro.Refazer()) DepoisDeTrocar();
    }

    // Desfazer um recorte muda o tamanho da imagem
    void DepoisDeTrocar()
    {
        if (ajustada) Ajustar();
        AtualizarBotoes();
        tela.Invalidate();
    }

    public void Copiar()
    {
        Assentar();
        if (App.CopiarEdicao(quadro.Imagem, quadro.Alterado))
        {
            quadro.MarcarSalvo();
            Avisar("\u2713  Copiada. Cole onde quiser com Ctrl+V.", false);
        }
        else Avisar("N\u00e3o consegui copiar. Tente de novo.", true);
    }

    void SalvarComo()
    {
        Assentar();
        using (SaveFileDialog d = new SaveFileDialog())
        {
            d.Title = "Salvar imagem como";
            d.Filter = "Imagem PNG (*.png)|*.png|Imagem JPG (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp";
            d.FileName = "imagem-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png";
            d.DefaultExt = "png";
            d.AddExtension = true;
            d.OverwritePrompt = true;
            d.RestoreDirectory = true;
            if (Directory.Exists(Config.PastaPrints)) d.InitialDirectory = Config.PastaPrints;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                Historico.Gravar(quadro.Imagem, d.FileName);
                quadro.MarcarSalvo();
                Avisar("\u2713  Salvo em " + d.FileName, false);
            }
            catch (Exception ex)
            {
                App.Log("Erro ao salvar edicao: " + ex.Message);
                Avisar("N\u00e3o consegui salvar: " + ex.Message, true);
            }
        }
    }

    // ---- Teclado ----
    protected override bool ProcessCmdKey(ref Message msg, Keys k)
    {
        if (caixaTexto != null)
        {
            if (k == Keys.Escape) { FecharTexto(false); return true; }
            if (k == Keys.Enter)  { FecharTexto(true); return true; }
            if (k == (Keys.Shift | Keys.Enter))
            {
                caixaTexto.SelectedText = "\r\n";
                return true;
            }
            return base.ProcessCmdKey(ref msg, k);
        }

        switch (k)
        {
            case Keys.Control | Keys.Z:              Desfazer(); return true;
            case Keys.Control | Keys.Y:
            case Keys.Control | Keys.Shift | Keys.Z: Refazer(); return true;
            case Keys.Control | Keys.C:              Copiar(); return true;
            case Keys.Control | Keys.S:              SalvarComo(); return true;
            case Keys.Control | Keys.V:              Colar(); return true;
            case Keys.Control | Keys.D0:
            case Keys.Control | Keys.NumPad0:        Ajustar(); return true;
            case Keys.OemOpenBrackets:               MudarTamanho(-1); return true;
            case Keys.OemCloseBrackets:              MudarTamanho(1); return true;
            case Keys.Enter:                         Assentar(); return true;
            case Keys.Delete:                        DescartarFlutuante(); return true;
            case Keys.Escape:
                if (desenhando) { desenhando = false; quadro.CancelarTraco(); tela.Invalidate(); }
                else if (recortando) { recortando = false; tela.Invalidate(); }
                else if (flutuante != null) DescartarFlutuante();
                else Close();
                return true;
            case Keys.V: TrocarFerramenta(Ferramenta.Mover); return true;
            case Keys.L: TrocarFerramenta(Ferramenta.Lapis); return true;
            case Keys.M: TrocarFerramenta(Ferramenta.Marca); return true;
            case Keys.E: TrocarFerramenta(Ferramenta.Borracha); return true;
            case Keys.B: TrocarFerramenta(Ferramenta.Borrar); return true;
            case Keys.A: TrocarFerramenta(Ferramenta.BorrarArea); return true;
            case Keys.S: TrocarFerramenta(Ferramenta.Seta); return true;
            case Keys.R: TrocarFerramenta(Ferramenta.Retangulo); return true;
            case Keys.T: TrocarFerramenta(Ferramenta.Texto); return true;
            case Keys.C: TrocarFerramenta(Ferramenta.Recortar); return true;
            case Keys.I: MenuImagem(); return true;
        }
        return base.ProcessCmdKey(ref msg, k);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space && !espaco && caixaTexto == null)
        {
            espaco = true;
            tela.Cursor = Cursors.Hand;
            InvalidarPincel(mouse);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space && espaco)
        {
            espaco = false;
            tela.Cursor = CursorDaFerramenta();
            InvalidarPincel(mouse);
        }
    }

    // ---- Abrir e fechar ----
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Ajustar();
        tela.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (saindo || e.CloseReason != CloseReason.UserClosing) return;

        FecharTexto(true);
        bool pendente = quadro.Alterado || flutuante != null;
        if (pendente && MessageBox.Show(this,
                "Fechar sem copiar nem salvar as altera\u00e7\u00f5es?",
                "Editor de imagem", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        saindo = true;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        App.Aparar();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            quadro.Dispose();
            if (flutuante != null) { flutuante.Dispose(); flutuante = null; }
            xadrez.Dispose();
            dicas.Dispose();
            apagaAviso.Dispose();
            icone.Dispose();
        }
        base.Dispose(disposing);
    }
}

// ---------------------------------------------------------------------
// 9. PROGRAMA: icone na bandeja, atalhos e colar
// ---------------------------------------------------------------------
class App : ApplicationContext
{
    const string ChaveIniciar = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string NomeIniciar  = "ClipHistorico";
    static readonly string TextoBandeja = "\u00c1rea de transfer\u00eancia";

    readonly Nucleo nucleo;
    readonly NotifyIcon bandeja;
    readonly Icon icone;
    readonly Timer repetir;
    readonly Timer limpeza;
    static App instancia;
    Janela janela;
    IntPtr anterior;
    uint ultimaSeq;
    TelaRecorte recorte;
    int tentativas;
    bool pausado;
    readonly SynchronizationContext contexto;

    public App()
    {
        // Devolve o resultado das gravacoes para a thread da janela
        contexto = new WindowsFormsSynchronizationContext();

        Config.Carregar();
        Historico.Carregar();

        nucleo = new Nucleo();
        nucleo.CopiaDetectada += delegate { LerAreaDeTransferencia(); };
        nucleo.AcaoPedida     += Executar;
        ultimaSeq = Nativo.GetClipboardSequenceNumber();

        // Se outro programa estiver usando a area de transferencia, tenta de novo
        repetir = new Timer();
        repetir.Interval = 150;
        repetir.Tick += delegate { repetir.Stop(); LerAreaDeTransferencia(); };

        // Acorda so quando o proximo item vence, em vez de ficar verificando
        instancia = this;
        limpeza = new Timer();
        limpeza.Tick += delegate { LimparVencidos(); };
        AgendarLimpeza();

        icone = Tema.CriarIcone();
        bandeja = new NotifyIcon();
        bandeja.Icon = icone;
        bandeja.Text = TextoBandeja;
        bandeja.ContextMenuStrip = CriarMenu();
        bandeja.MouseClick += delegate(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Alternar(Janela.PgHistorico);
        };
        bandeja.Visible = true;

        if (!nucleo.GanchoOk)
            bandeja.ShowBalloonTip(5000, TextoBandeja,
                "N\u00e3o foi poss\u00edvel ativar os atalhos de teclado. Clique no \u00edcone para abrir.", ToolTipIcon.Warning);

        Aparar();
    }

    ContextMenuStrip CriarMenu()
    {
        ContextMenuStrip menu = CoresMenu.NovoMenu();

        ToolStripMenuItem abrir = new ToolStripMenuItem("Abrir hist\u00f3rico");
        abrir.Font = Tema.Negrito;
        abrir.Click += delegate { Abrir(Janela.PgHistorico); };

        ToolStripMenuItem salvos = new ToolStripMenuItem("Textos salvos");
        salvos.Click += delegate { Abrir(Janela.PgSalvos); };

        ToolStripMenuItem emotes = new ToolStripMenuItem("Emotes");
        emotes.Click += delegate { Abrir(Janela.PgEmotes); };

        ToolStripMenuItem config = new ToolStripMenuItem("Configura\u00e7\u00f5es");
        config.Click += delegate { Abrir(Janela.PgConfig); };

        ToolStripMenuItem recortar = new ToolStripMenuItem("Recortar parte da tela");
        recortar.Click += delegate { RecortarTela(); };

        ToolStripMenuItem pausar = new ToolStripMenuItem("Pausar captura");
        pausar.CheckOnClick = true;
        pausar.CheckedChanged += delegate
        {
            pausado = pausar.Checked;
            if (!pausado) ultimaSeq = Nativo.GetClipboardSequenceNumber();
            bandeja.Text = pausado ? TextoBandeja + " (pausado)" : TextoBandeja;
        };

        ToolStripMenuItem pasta = new ToolStripMenuItem("Abrir pasta de prints");
        pasta.Click += delegate { AbrirPasta(Config.PastaPrints); };

        ToolStripMenuItem limpar = new ToolStripMenuItem("Limpar hist\u00f3rico");
        limpar.Click += delegate
        {
            DialogResult r = MessageBox.Show("Apagar todo o hist\u00f3rico deste computador?\n(Os textos salvos continuam.)",
                TextoBandeja, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            Historico.LimparTudo();
            if (janela != null) janela.Recarregar();
            Aparar();
        };

        ToolStripMenuItem sair = new ToolStripMenuItem("Sair");
        sair.Click += delegate { ExitThread(); };

        menu.Items.AddRange(new ToolStripItem[] {
            abrir, salvos, emotes, recortar, config, new ToolStripSeparator(),
            pausar, pasta, new ToolStripSeparator(), limpar, sair });
        return menu;
    }

    // ---- Atalhos ----
    void Executar(int acao)
    {
        switch (acao)
        {
            case Config.AbrirHistorico: Alternar(Janela.PgHistorico); break;
            case Config.AbrirEmotes:    Alternar(Janela.PgEmotes); break;
            case Config.PrintJanela:    TirarPrint(false); break;
            case Config.PrintTela:      TirarPrint(true); break;
            case Config.RecortarTela:   RecortarTela(); break;
        }
    }

    // ---- Area de transferencia ----
    void LerAreaDeTransferencia()
    {
        if (pausado) return;
        uint seq = Nativo.GetClipboardSequenceNumber();
        if (seq == ultimaSeq) return;

        try
        {
            if (Clipboard.ContainsText())
            {
                Historico.AdicionarTexto(Clipboard.GetText());
            }
            else if (Clipboard.ContainsImage())
            {
                Image img = Clipboard.GetImage();
                if (img != null) GuardarImagem(img, null);
            }
            ultimaSeq = seq;
            tentativas = 0;
            if (janela != null) janela.Recarregar();
        }
        catch (Exception ex)
        {
            if (++tentativas < 5) { repetir.Start(); return; }
            Log("Falha ao ler a area de transferencia: " + ex.Message);
            ultimaSeq = seq;
            tentativas = 0;
        }
    }

    // ---- Prazo do historico ----
    void LimparVencidos()
    {
        if (Historico.Expirar(DateTime.Now) > 0)
        {
            if (janela != null) janela.Recarregar();
            Aparar();
        }
        AgendarLimpeza();
    }

    void AgendarLimpeza()
    {
        limpeza.Stop();
        if (Config.PrazoHoras <= 0) return;
        DateTime? vence = Historico.ProximoVencimento();
        double ms = vence.HasValue ? (vence.Value - DateTime.Now).TotalMilliseconds + 1000 : 3600000;
        limpeza.Interval = (int)Math.Max(60000, Math.Min(3600000, ms));
        limpeza.Start();
    }

    public static void PrazoMudou()
    {
        if (instancia != null) instancia.LimparVencidos();
    }

    // ---- Print ----
    void TirarPrint(bool telaInteira)
    {
        try
        {
            Entregar(Captura.Tirar(telaInteira), Captura.CaminhoNovo());
        }
        catch (Exception ex) { Log("Erro ao tirar print: " + ex.Message); }
    }

    // A imagem passa a pertencer a este metodo
    void Entregar(Bitmap bmp, string arquivoPrint)
    {
        try
        {
            Clipboard.SetDataObject(bmp, true, 10, 100);
            // Ja vai direto para o historico: nao precisa ler de volta
            ultimaSeq = Nativo.GetClipboardSequenceNumber();
        }
        catch (Exception ex) { Log("Erro ao copiar o print: " + ex.Message); }

        if (Config.SomAoCapturar) Som.Tocar();
        GuardarImagem(bmp, arquivoPrint);
    }

    // Vai para a area de transferencia e para o historico. O foco volta para
    // onde estava, para o jogo nao ficar minimizado depois do recorte.
    void RecortarTela()
    {
        if (recorte != null) return;   // ja esta aberto
        try
        {
            IntPtr antes = Nativo.GetForegroundWindow();
            Rectangle area;
            Bitmap foto = Captura.TirarTudo(out area);
            recorte = new TelaRecorte(foto, area);
            recorte.FormClosed += delegate
            {
                Bitmap feito = recorte.Resultado;
                TelaRecorte fechada = recorte;
                recorte = null;
                contexto.Post(delegate { fechada.Dispose(); Aparar(); }, null);
                if (antes != IntPtr.Zero) Nativo.SetForegroundWindow(antes);
                if (feito != null) Entregar(feito, null);
            };
            recorte.Mostrar();
        }
        catch (Exception ex)
        {
            recorte = null;
            Log("Erro ao abrir o recorte: " + ex.ToString());
        }
    }

    // Grava a imagem fora da thread da janela: um PNG grande leva centenas de
    // milissegundos. A imagem passa a pertencer a este metodo. Com arquivoPrint,
    // grava tambem na pasta de prints. A thread nao e de fundo de proposito:
    // sair do programa logo depois de um print nao pode perder o arquivo.
    void GuardarImagem(Image img, string arquivoPrint)
    {
        string destino = pausado ? null : Historico.NovoCaminho(".png");
        Thread t = new Thread(delegate()
        {
            Item novo = null;
            try
            {
                string feito = null;
                if (arquivoPrint != null)
                {
                    img.Save(arquivoPrint, ImageFormat.Png);
                    feito = arquivoPrint;
                }
                if (destino != null)
                {
                    Directory.CreateDirectory(Config.Pasta);
                    if (feito != null) File.Copy(feito, destino, true);
                    else img.Save(destino, ImageFormat.Png);
                    novo = Item.DeImagem(destino, img);
                }
            }
            catch (Exception ex) { Log("Erro ao gravar imagem: " + ex.Message); }
            finally { img.Dispose(); }

            if (novo == null) { Aparar(); return; }
            try
            {
                contexto.Post(delegate
                {
                    Historico.Inserir(novo);
                    if (janela != null) janela.Recarregar();
                }, null);
            }
            catch { }
            Aparar();
        }, 512 * 1024);
        t.Priority = ThreadPriority.BelowNormal;
        t.Start();
    }

    // ---- Editor de imagem ----
    // Cada editor e uma janela independente; a imagem passa a ser dele
    public static void Editar(Bitmap img)
    {
        try
        {
            EditorImagem e = new EditorImagem(img);
            e.Show();
            e.Activate();
            Nativo.SetForegroundWindow(e.Handle);
        }
        catch (Exception ex) { Log("Erro ao abrir o editor: " + ex.ToString()); }
    }

    // Se mudou, entra no historico como imagem nova: a original continua la.
    public static bool CopiarEdicao(Bitmap img, bool alterada)
    {
        try
        {
            PorImagem(img);
            if (alterada && instancia != null) instancia.GuardarImagem(new Bitmap(img), null);
            return true;
        }
        catch (Exception ex)
        {
            Log("Erro ao copiar a edicao: " + ex.Message);
            return false;
        }
    }

    // ---- Janela ----
    void Alternar(int pagina)
    {
        if (janela != null && janela.Pagina == pagina) janela.Fechar();
        else Abrir(pagina);
    }

    void Abrir(int pagina)
    {
        if (janela != null)
        {
            janela.MostrarPagina(pagina);
            janela.Activate();
            return;
        }
        anterior = Nativo.GetForegroundWindow();
        Historico.Expirar(DateTime.Now);
        janela = new Janela(nucleo, pagina);
        janela.FormClosed += delegate { JanelaFechou(); };
        janela.Show();
        janela.Activate();
        Nativo.SetForegroundWindow(janela.Handle);
    }

    void JanelaFechou()
    {
        object resultado = janela.Resultado;
        janela = null;   // a janela se libera sozinha ao fechar
        if (resultado != null) Usar(resultado);
        Aparar();
    }

    // Coloca o item escolhido na area de transferencia
    void Usar(object resultado)
    {
        EnvioSalvo envio = resultado as EnvioSalvo;
        if (envio != null) { UsarSalvo(envio); return; }

        Item item = resultado as Item;
        try
        {
            if (item == null)
            {
                Clipboard.SetDataObject((string)resultado, true, 10, 100);
            }
            else if (item.Imagem)
            {
                using (Bitmap img = Historico.LerImagem(item.Caminho))
                    Clipboard.SetDataObject(img, true, 10, 100);
            }
            else
            {
                Clipboard.SetDataObject(item.Texto, true, 10, 100);
            }
        }
        catch (Exception ex)
        {
            Log("Erro ao copiar: " + ex.Message);
            return;
        }

        ultimaSeq = Nativo.GetClipboardSequenceNumber();   // nao registra de novo no historico
        if (item != null) Historico.ParaOTopo(item);

        if (anterior != IntPtr.Zero) Nativo.SetForegroundWindow(anterior);
        if (Config.ColarAuto)
        {
            System.Threading.Thread.Sleep(120);
            SendKeys.SendWait("^v");
        }
    }

    // Imagem e texto nao viajam juntos no Ctrl+V: quem cola fica com um so.
    // Entao cola a imagem e, com a previa do WhatsApp aberta, cola o texto na
    // legenda. Nunca aperta Enter: quem confere e envia e a pessoa.
    void UsarSalvo(EnvioSalvo e)
    {
        try
        {
            using (Bitmap img = Historico.LerImagem(e.Salvo.Foto.Caminho)) PorImagem(img);
        }
        catch (Exception ex)
        {
            Log("Erro ao copiar imagem do salvo: " + ex.Message);
            return;
        }

        if (anterior != IntPtr.Zero) Nativo.SetForegroundWindow(anterior);
        if (!e.ComTexto && !Config.ColarAuto) return;
        System.Threading.Thread.Sleep(120);
        SendKeys.SendWait("^v");
        if (e.ComTexto) ColarLegenda(e.Salvo.Texto, anterior, EsperaLegenda, null);
    }

    const int EsperaLegenda = 1200;   // tempo da previa da imagem abrir

    // Se a pessoa mudou de janela enquanto esperava, o texto fica so copiado:
    // nao vai parar em outra conversa ou programa.
    public static void ColarLegenda(string texto, IntPtr janelaAlvo, int ms, Action fim)
    {
        Timer t = new Timer();
        t.Interval = ms;
        t.Tick += delegate
        {
            t.Stop();
            t.Dispose();
            try
            {
                Clipboard.SetDataObject(texto, true, 10, 100);
                if (instancia != null) instancia.ultimaSeq = Nativo.GetClipboardSequenceNumber();
                if (Nativo.GetForegroundWindow() == janelaAlvo) SendKeys.SendWait("^v");
            }
            catch (Exception ex) { Log("Erro ao colar a legenda: " + ex.Message); }
            if (fim != null) fim();
        };
        t.Start();
    }

    // Vai como bitmap sobre branco, que todo programa cola, e tambem como PNG,
    // para quem aceita transparencia. Nao entra de novo no historico.
    public static void PorImagem(Image img)
    {
        using (Bitmap plano = Historico.SemTransparencia(img))
        using (MemoryStream png = new MemoryStream())
        {
            img.Save(png, ImageFormat.Png);
            DataObject d = new DataObject();
            d.SetImage(plano);
            d.SetData("PNG", false, png);
            Clipboard.SetDataObject(d, true, 10, 100);
        }
        if (instancia != null) instancia.ultimaSeq = Nativo.GetClipboardSequenceNumber();
    }

    // ---- Iniciar com o Windows ----
    public static bool IniciaComWindows()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ChaveIniciar))
                return k != null && k.GetValue(NomeIniciar) != null;
        }
        catch { return false; }
    }

    public static void DefinirIniciar(bool ligar)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ChaveIniciar))
            {
                if (ligar) k.SetValue(NomeIniciar, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(NomeIniciar, false);
            }
        }
        catch (Exception ex) { Log("Erro ao configurar inicializacao: " + ex.Message); }
    }

    // ---- Utilidades ----

    public static void AbrirPasta(string pasta)
    {
        try
        {
            Directory.CreateDirectory(pasta);
            Process.Start("explorer.exe", "\"" + pasta + "\"");
        }
        catch (Exception ex) { Log("Erro ao abrir pasta: " + ex.Message); }
    }

    // Devolve ao Windows a memoria que nao esta em uso. Espera 1,5 s de calma
    // e roda fora da janela: varios pedidos seguidos viram uma limpeza so.
    static System.Threading.Timer aparador;

    public static void Aparar()
    {
        if (aparador == null)
            aparador = new System.Threading.Timer(delegate
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                try { Nativo.EmptyWorkingSet(Nativo.GetCurrentProcess()); } catch { }
            }, null, Timeout.Infinite, Timeout.Infinite);
        aparador.Change(1500, Timeout.Infinite);
    }

    public static void Log(string mensagem)
    {
        try
        {
            Directory.CreateDirectory(Config.PastaDados);
            string arq = Path.Combine(Config.PastaDados, "erros.log");
            if (File.Exists(arq) && new FileInfo(arq).Length > 64 * 1024) File.Delete(arq);
            File.AppendAllText(arq, "[" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + "] " + mensagem + "\r\n");
        }
        catch { }
    }

    protected override void ExitThreadCore()
    {
        if (janela != null) janela.Close();
        repetir.Dispose();
        limpeza.Dispose();
        bandeja.Visible = false;
        bandeja.Dispose();
        nucleo.Dispose();
        icone.Dispose();
        base.ExitThreadCore();
    }
}


// ---------------------------------------------------------------------
// 10. INICIO
// ---------------------------------------------------------------------
static class Programa
{
    [STAThread]
    static void Main(string[] args)
    {
#if TESTE
        Teste.Rodar(args);
        return;
#else
        bool primeiro;
        using (System.Threading.Mutex unico = new System.Threading.Mutex(true, "ClipHistoricoExe", out primeiro))
        {
            if (!primeiro) return;   // ja esta aberto

            System.Threading.Mutex antigo;
            if (System.Threading.Mutex.TryOpenExisting("ClipHistoricoPS", out antigo))
            {
                antigo.Dispose();
                MessageBox.Show(
                    "A vers\u00e3o antiga (PowerShell) ainda est\u00e1 aberta.\n\n" +
                    "Clique com o bot\u00e3o direito no \u00edcone dela perto do rel\u00f3gio, escolha Sair e abra este programa de novo.",
                    "\u00c1rea de transfer\u00eancia", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                App.Log(e.Exception.ToString());
            };

            Application.Run(new App());
            GC.KeepAlive(unico);
        }
#endif
    }
}
