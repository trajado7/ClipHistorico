// =====================================================================
//  ClipHistorico.cs
//  Area de transferencia turbinada para Windows
//
//  Abas: Historico (textos e imagens copiados), Salvos (seus textos fixos)
//  e Emotes (com busca em portugues, como o Win + . do Windows).
//
//  Atalhos padrao (mude na engrenagem da janela):
//    Win + V ............ historico
//    Win + . ............ emotes
//    Ctrl + Alt + J ..... print da janela ativa (funciona dentro de jogos)
//    Ctrl + Alt + K ..... print da tela inteira
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
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;


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

    // Acoes que tem atalho
    public const int AbrirHistorico = 0, AbrirEmotes = 1, PrintJanela = 2, PrintTela = 3;

    public static readonly string[] NomesAcoes =
    {
        "Abrir a \u00e1rea de transfer\u00eancia",
        "Abrir os emotes",
        "Print da janela ativa",
        "Print da tela inteira"
    };

    public static readonly Atalho[] Padrao =
    {
        new Atalho(Atalho.Win, 0x56),                 // Win + V
        new Atalho(Atalho.Win, 0xBE),                 // Win + .
        new Atalho(Atalho.Ctrl | Atalho.Alt, 0x4A),   // Ctrl + Alt + J
        new Atalho(Atalho.Ctrl | Atalho.Alt, 0x4B)    // Ctrl + Alt + K
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
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
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
    public static readonly Font Emote    = new Font("Segoe UI Emoji", 17f);
    public static readonly Font EmoteMeio = new Font("Segoe UI Emoji", 13f);
    public static readonly Font EmoteGrande = new Font("Segoe UI Emoji", 20f);

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
        linha = Regex.Replace(linha.Trim(), @"\s+", " ");
        return linha.Length > 200 ? linha.Substring(0, 200) : linha;
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
    public Bitmap   Mini;      // miniatura (so imagens)
    string texto;              // null quando o texto e grande e fica so no disco

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
            using (FileStream fs = File.OpenRead(caminho))
            using (Image img = Image.FromStream(fs))
                return DeImagem(caminho, img);
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
        it.Mini    = Miniatura(img);
        return it;
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
        arquivos.AddRange(Directory.GetFiles(Config.Pasta, "*.txt"));
        arquivos.AddRange(Directory.GetFiles(Config.Pasta, "*.png"));
        arquivos.Sort(delegate(string a, string b) { return string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)); });

        foreach (string f in arquivos)
        {
            try { Itens.Add(Item.DeArquivo(f)); }
            catch (Exception ex) { App.Log("Nao foi possivel ler " + f + ": " + ex.Message); }
        }
        Limitar();
    }

    static string NovoCaminho(string extensao)
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

    public static void AdicionarImagem(Image img)
    {
        Directory.CreateDirectory(Config.Pasta);
        string p = NovoCaminho(".png");
        img.Save(p, ImageFormat.Png);
        Itens.Insert(0, Item.DeImagem(p, img));
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
        if (it.Mini != null) { it.Mini.Dispose(); it.Mini = null; }
        Itens.Remove(it);
    }

    // Apaga TUDO: lista e todos os arquivos da pasta
    public static void LimparTudo()
    {
        foreach (Item it in Itens) if (it.Mini != null) it.Mini.Dispose();
        Itens.Clear();
        if (!Directory.Exists(Config.Pasta)) return;
        foreach (string f in Directory.GetFiles(Config.Pasta))
        {
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
}

static class Salvos
{
    static List<Salvo> itens;   // carregado so quando a aba e aberta

    static string Pasta { get { return Path.Combine(Config.PastaDados, "salvos"); } }

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
        s.Titulo  = Tema.TituloDe(texto);
        s.Hora    = Item.HoraDoNome(caminho);
        return s;
    }

    public static Salvo Adicionar(string texto)
    {
        Directory.CreateDirectory(Pasta);
        DateTime agora = DateTime.Now;
        string p;
        do
        {
            p = Path.Combine(Pasta, agora.ToString("yyyyMMdd_HHmmssfff") + ".txt");
            agora = agora.AddMilliseconds(1);
        } while (File.Exists(p));

        File.WriteAllText(p, texto, new UTF8Encoding(true));
        Salvo s = Criar(p, texto);
        Itens.Insert(0, s);
        return s;
    }

    public static void Alterar(Salvo s, string texto)
    {
        File.WriteAllText(s.Caminho, texto, new UTF8Encoding(true));
        s.Texto  = texto;
        s.Titulo = Tema.TituloDe(texto);
    }

    public static void Remover(Salvo s)
    {
        try { File.Delete(s.Caminho); } catch { }
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
    public bool Gravando;                         // true = a proxima combinacao vai para TeclaGravada

    IntPtr gancho = IntPtr.Zero;
    Nativo.HookProc proc;   // guardado para o coletor de lixo nao apagar
    int teclaEngolida;      // tecla escondida do Windows ate ser solta

    public Nucleo()
    {
        CreateParams cp = new CreateParams();
        cp.Parent = new IntPtr(-3);   // janela invisivel, so recebe mensagens
        CreateHandle(cp);

        // Avisado pelo Windows a cada copia (sem ficar verificando)
        Nativo.AddClipboardFormatListener(Handle);

        RegistrarReservas();

        // Gancho de teclado: pega os atalhos antes do Windows e dos jogos
        proc = Gancho;
        gancho = Nativo.SetWindowsHookEx(13, proc, Nativo.GetModuleHandle(null), 0);
        GanchoOk = gancho != IntPtr.Zero;
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
                    Nativo.PostMessage(Handle, WM_GRAVOU, new IntPtr(vk), new IntPtr(mods));
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

                    Nativo.PostMessage(Handle, WM_ACAO, new IntPtr(i), IntPtr.Zero);
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
        if (gancho != IntPtr.Zero) { Nativo.UnhookWindowsHookEx(gancho); gancho = IntPtr.Zero; }
        Nativo.RemoveClipboardFormatListener(Handle);
        for (int i = 0; i < Config.Atalhos.Length; i++) Nativo.UnregisterHotKey(Handle, i + 1);
        DestroyHandle();
    }
}


// ---------------------------------------------------------------------
// 6b. PRINT (janela ativa ou tela inteira)
// ---------------------------------------------------------------------

static class Captura
{
    // Tira o print e devolve o caminho do arquivo salvo (ou null)
    public static string Tirar(bool telaInteira)
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

            using (Bitmap bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);

                // Alguns jogos aparecem pretos na copia de tela: tenta pela janela
                if (janela && QuasePreta(bmp)) TentarPrintWindow(h, area, bmp);

                Directory.CreateDirectory(Config.PastaPrints);
                string arq = Path.Combine(Config.PastaPrints,
                    "captura-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
                for (int n = 2; File.Exists(arq); n++)
                    arq = Path.Combine(Config.PastaPrints,
                        "captura-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + n + ".png");

                bmp.Save(arq, ImageFormat.Png);
                Clipboard.SetDataObject(bmp, true, 10, 100);
                return arq;
            }
        }
        finally
        {
            if (dpiAntigo != IntPtr.Zero)
                try { Nativo.SetThreadDpiAwarenessContext(dpiAntigo); } catch { }
        }
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
            sobre = i;
            Invalidate();
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
        int c = Colunas;
        int primeira = Math.Max(0, (rolagem - Margem) / Celula) * c;
        int ultima = Math.Min(itens.Count - 1, ((rolagem + ClientSize.Height) / Celula + 1) * c);

        for (int i = primeira; i <= ultima; i++)
        {
            Rectangle r = Ret(i);
            if (i == selecionado || i == sobre)
            {
                using (GraphicsPath p = Tema.Arredondado(Rectangle.Inflate(r, -2, -2), 6))
                    g.FillPath(Tema.Pincel(i == selecionado ? Tema.Selecao : Tema.Campo), p);
            }
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
    ToolTip dicas;

    // Historico
    ListaCartoes listaHist;
    Label vazioHist, infoHist;
    TextBox previaTexto;
    PictureBox previaImagem;
    readonly List<Item> visHist = new List<Item>();
    bool confirmarLimpar;

    // Salvos
    ListaCartoes listaSalvos;
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
        busca.TextChanged += delegate { Filtrar(); };
        caixaBusca.Controls.Add(busca);
        barraBusca.Controls.Add(caixaBusca);

        // ---- Rodape ----
        rodape = NovoRotulo("", Tema.Pequena, Tema.Suave, Tema.Fundo);
        rodape.Dock = DockStyle.Bottom;
        rodape.Height = 36;
        rodape.TextAlign = ContentAlignment.MiddleLeft;
        rodape.Padding = new Padding(14, 0, 0, 0);

        // ---- Paginas ----
        Panel conteudo = NovoPainel(DockStyle.Fill, Tema.Fundo);
        conteudo.Padding = new Padding(16, 0, 16, 0);
        paginas = new Panel[] { CriarHistorico(), CriarSalvos(), CriarEmotes(), CriarConfig() };
        foreach (Panel p in paginas) { p.Visible = false; conteudo.Controls.Add(p); }

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
        l.MouseEnter += delegate { l.BackColor = fundoSob; };
        l.MouseLeave += delegate { l.BackColor = fundo; };
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
        return t;
    }

    static string Quebras(string t)
    {
        if (t.Length > 200000) t = t.Substring(0, 200000) + "\n...";
        return Regex.Replace(t, "(?<!\r)\n", "\r\n");
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

        pagina = p;
        for (int i = 0; i < paginas.Length; i++) paginas[i].Visible = i == p;
        for (int i = 0; i < abas.Length; i++) abas[i].Ativa = i == p;
        botaoConfig.ForeColor = p == PgConfig ? Tema.Destaque : Tema.Suave;
        barraBusca.Visible = p != PgConfig;
        botaoAcao.Visible = p == PgHistorico || p == PgSalvos;
        botaoAcao.Text = p == PgSalvos ? "+  Novo texto" : "Limpar tudo";

        switch (p)
        {
            case PgHistorico:
                rodape.Text = "Clique ou Enter  copiar" + Tema.Ponto + "Bot\u00e3o direito  mais op\u00e7\u00f5es" +
                              Tema.Ponto + "Shift+Del  apagar" + Tema.Ponto + "Esc  fechar";
                break;
            case PgSalvos:
                rodape.Text = "Clique ou Enter  copiar" + Tema.Ponto + "Bot\u00e3o direito  editar ou apagar" +
                              Tema.Ponto + "Ctrl+S  salvar a edi\u00e7\u00e3o";
                break;
            case PgEmotes:
                rodape.Text = "Clique ou Enter  copiar" + Tema.Ponto + "Setas  navegar" + Tema.Ponto + "Digite para buscar";
                break;
            default:
                rodape.Text = "As mudan\u00e7as s\u00e3o salvas na hora";
                AtualizarConfig();
                break;
        }

        if (busca.Text.Length > 0) busca.Text = "";   // dispara Filtrar
        else Filtrar();
        AtualizarDicaBusca();
        if (IsHandleCreated && p != PgConfig) busca.Focus();
    }

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
        listaHist.MouseClick += delegate(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int i = listaHist.IndexFromPoint(e.Location);
            if (i >= 0) { listaHist.SelectedIndex = i; Escolher(visHist[i]); }
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
        previaTexto = NovoTextoPrevia();
        previaImagem = new PictureBox();
        previaImagem.Dock = DockStyle.Fill;
        previaImagem.SizeMode = PictureBoxSizeMode.Zoom;
        previaImagem.BackColor = Tema.Painel;
        previaImagem.Visible = false;
        direita.Controls.AddRange(new Control[] { previaTexto, previaImagem, infoHist });

        pg.Controls.AddRange(new Control[] { direita, espaco, esquerda });
        return pg;
    }

    ContextMenuStrip MenuHistorico()
    {
        ContextMenuStrip m = CoresMenu.NovoMenu();
        ToolStripMenuItem copiar = new ToolStripMenuItem("Copiar");
        ToolStripMenuItem salvar = new ToolStripMenuItem("Salvar na aba Salvos");
        ToolStripMenuItem apagar = new ToolStripMenuItem("Apagar");
        copiar.Click += delegate { Item it = ItemHistAtual(); if (it != null) Escolher(it); };
        salvar.Click += delegate
        {
            Item it = ItemHistAtual();
            if (it == null || it.Imagem) return;
            Salvos.Adicionar(it.Texto);
            infoHist.Text = "\u2713  Salvo na aba Salvos";
        };
        apagar.Click += delegate { ApagarHistorico(); };
        m.Items.AddRange(new ToolStripItem[] { copiar, salvar, new ToolStripSeparator(), apagar });
        m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
        {
            Item it = ItemHistAtual();
            if (it == null) { e.Cancel = true; return; }
            salvar.Enabled = !it.Imagem;
        };
        return m;
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
            try { previaImagem.Image = Historico.LerImagem(it.Caminho); } catch { }
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
            c.Info = s.Texto.Length + " caracteres";
            c.Icone = "\u2605";
            c.FonteIcone = Tema.Engrenagem;
        };
        listaSalvos.SelectedIndexChanged += delegate { if (!editando) PreviaSalvo(); };
        listaSalvos.MouseClick += delegate(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int i = listaSalvos.IndexFromPoint(e.Location);
            if (i < 0) return;
            if (editando) SalvarEdicao();
            Escolher(visSalvos[Math.Min(i, visSalvos.Count - 1)].Texto);
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
        barraEdicao.Controls.AddRange(new Control[] { cancelar, vao, salvar });
        barraEdicao.Visible = false;

        direita.Controls.AddRange(new Control[] { editor, barraEdicao, infoSalvos });
        pg.Controls.AddRange(new Control[] { direita, espaco, esquerda });
        return pg;
    }

    ContextMenuStrip MenuSalvos()
    {
        ContextMenuStrip m = CoresMenu.NovoMenu();
        ToolStripMenuItem copiar = new ToolStripMenuItem("Copiar");
        ToolStripMenuItem editar = new ToolStripMenuItem("Editar");
        ToolStripMenuItem apagar = new ToolStripMenuItem("Apagar");
        copiar.Click += delegate { Salvo s = SalvoAtual(); if (s != null) Escolher(s.Texto); };
        editar.Click += delegate { Salvo s = SalvoAtual(); if (s != null) ComecarEdicao(s); };
        apagar.Click += delegate { ApagarSalvo(); };
        m.Items.AddRange(new ToolStripItem[] { copiar, editar, new ToolStripSeparator(), apagar });
        m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
        {
            if (SalvoAtual() == null) e.Cancel = true;
        };
        return m;
    }

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
        vazioSalvos.Text = filtro.Length > 0 ? "Nenhum resultado" : "Nenhum texto salvo ainda.\nClique em \"+ Novo texto\" no topo.";

        if (tem) listaSalvos.SelectedIndex = 0;
        else if (!editando) PreviaSalvo();
    }

    void PreviaSalvo()
    {
        Salvo s = SalvoAtual();
        editor.Text = s == null ? "" : Quebras(s.Texto);
        infoSalvos.Text = s == null ? "" :
            "TEXTO SALVO" + Tema.Ponto + s.Texto.Length + " caracteres" + Tema.Ponto + "bot\u00e3o direito para editar";
    }

    void ComecarEdicao(Salvo s)
    {
        if (editando) SalvarEdicao();
        editando = true;
        emEdicao = s;
        editor.ReadOnly = false;
        editor.BackColor = Tema.Campo;
        editor.Text = s == null ? "" : Quebras(s.Texto);
        infoSalvos.Text = s == null ? "NOVO TEXTO" + Tema.Ponto + "escreva e clique em Salvar (Ctrl+S)"
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
        SairDaEdicao();

        if (t.Trim().Length > 0)
        {
            if (alvo == null) alvo = Salvos.Adicionar(t);
            else Salvos.Alterar(alvo, t);
        }
        RecarregarSalvos();
        int i = alvo == null ? -1 : visSalvos.IndexOf(alvo);
        if (i >= 0) listaSalvos.SelectedIndex = i;
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
            Label b = NovoRotulo(k == 0 ? "\U0001F552" : Emotes.IconesCategorias[k - 1], Tema.EmoteMeio, Tema.Suave, Tema.Painel);
            b.Dock = DockStyle.Left;
            b.Width = 46;
            b.TextAlign = ContentAlignment.MiddleCenter;
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
        statusIcone = NovoRotulo("", Tema.EmoteGrande, Tema.Texto, Tema.Painel);
        statusIcone.Dock = DockStyle.Left;
        statusIcone.Width = 60;
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
        if (busca.Text.Length > 0) busca.Text = "";   // dispara Filtrar
        else RecarregarEmotes();
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
        if (e == null) { statusIcone.Text = ""; statusNome.Text = ""; return; }
        statusIcone.Text = e.Simbolo;
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
        y += 14;

        pg.Controls.Add(Posicionar(NovoRotulo("Prints: " + Config.PastaPrints, Tema.Pequena, Tema.Suave, Tema.Painel), 24, y, 640, 20));
        y += 22;
        pg.Controls.Add(Posicionar(NovoRotulo("Hist\u00f3rico: " + Config.Pasta, Tema.Pequena, Tema.Suave, Tema.Painel), 24, y, 640, 20));
        y += 22;
        pg.Controls.Add(Posicionar(NovoRotulo("Salvos e configura\u00e7\u00f5es: " + Config.PastaDados, Tema.Pequena, Tema.Suave, Tema.Painel), 24, y, 640, 20));

        return pg;
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
            switch (k)
            {
                case Keys.Enter:
                    if (pagina == PgHistorico) { Item it = ItemHistAtual(); if (it != null) Escolher(it); }
                    else { Salvo s = SalvoAtual(); if (s != null) Escolher(s.Texto); }
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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AtualizarDicaBusca();
        Nativo.TemaEscuro(previaTexto.Handle);
        Nativo.TemaEscuro(editor.Handle);
        Activate();
        if (pagina != PgConfig) busca.Focus();
    }

    // Fecha ao clicar fora, como o Win + V original
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (gravando < 0) Fechar();
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
            Emotes.Liberar();
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
    Janela janela;
    IntPtr anterior;
    uint ultimaSeq;
    int tentativas;
    bool pausado;

    public App()
    {
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

        ToolStripMenuItem pausar = new ToolStripMenuItem("Pausar captura");
        pausar.CheckOnClick = true;
        pausar.CheckedChanged += delegate
        {
            pausado = pausar.Checked;
            if (!pausado) ultimaSeq = Nativo.GetClipboardSequenceNumber();
            bandeja.Text = pausado ? TextoBandeja + " (pausado)" : TextoBandeja;
        };

        ToolStripMenuItem pasta = new ToolStripMenuItem("Abrir pasta de prints");
        pasta.Click += delegate
        {
            Directory.CreateDirectory(Config.PastaPrints);
            Process.Start("explorer.exe", "\"" + Config.PastaPrints + "\"");
        };

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
            abrir, salvos, emotes, config, new ToolStripSeparator(),
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
                using (Image img = Clipboard.GetImage())
                    if (img != null) Historico.AdicionarImagem(img);
                Aparar();
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

    // ---- Print ----
    void TirarPrint(bool telaInteira)
    {
        try
        {
            if (Captura.Tirar(telaInteira) != null && Config.SomAoCapturar)
                System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex) { Log("Erro ao tirar print: " + ex.Message); }
        Aparar();
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

    // Devolve ao Windows a memoria que nao esta em uso
    public static void Aparar()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        try { Nativo.EmptyWorkingSet(Nativo.GetCurrentProcess()); } catch { }
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
    static void Main()
    {
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
    }
}
