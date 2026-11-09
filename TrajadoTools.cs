// =====================================================================
//  TrajadoTools.cs  (Trajado Tools; ate a versao 1.6 se chamava ClipHistorico)
//  Area de transferencia, prints, clipes e gravacao de tela para Windows
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
//    Alt + Z ............ galeria de prints e clipes (como a da NVIDIA)
//    Alt + F10 .......... salvar os ultimos segundos da tela (replay)
//    Alt + Shift + F10 .. ligar ou desligar o replay
//    Alt + F9 ........... iniciar ou parar a gravacao da tela
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

    public const string Nome = "Trajado Tools";

    // Pastas
    public static readonly string Pasta = PastaDoApp(Environment.SpecialFolder.UserProfile);        // historico
    public static readonly string PastaPrints = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PRINTS");            // prints
    public static readonly string PastaDados = PastaDoApp(Environment.SpecialFolder.ApplicationData); // config, salvos, recentes
                                                                                                    // (o "Limpar tudo" nao mexe aqui)

    // O app se chamava ClipHistorico: na primeira abertura a pasta antiga vira
    // TrajadoTools (renomear na mesma unidade e instantaneo e nao copia nada).
    // Se nao der (arquivo aberto), continua usando a antiga.
    public static string PastaDoApp(Environment.SpecialFolder raiz)
    {
        string b = Environment.GetFolderPath(raiz);
        string nova = Path.Combine(b, "TrajadoTools"), velha = Path.Combine(b, "ClipHistorico");
        if (Directory.Exists(nova) || !Directory.Exists(velha)) return nova;
#if TESTE
        return velha;   // os testes nunca mexem nas pastas de verdade
#else
        try { Directory.Move(velha, nova); return nova; }
        catch { return velha; }
#endif
    }

    // null = pode usar; senao, o motivo
    public static string ProblemaDoAtalho(int i, Atalho a)
    {
        if ((a.Mods & ~Atalho.Shift) == 0 && !Atalho.PodeSozinha(a.Vk))
            return "Use junto com Ctrl, Alt ou Win (ex.: Ctrl + Alt + " + Atalho.NomeTecla(a.Vk) + ").";
        for (int j = 0; j < Atalhos.Length; j++)
            if (j != i && Atalhos[j].Igual(a)) return a + " já está em: " + NomesAcoes[j];
        return null;
    }
    // Opcoes (mudam pela engrenagem)
    public static bool ColarAuto     = false;
    public static bool SomAoCapturar = true;

    // Prazo do historico em horas (0 = nunca apaga por tempo)
    public static int PrazoHoras = 24;
    public static readonly int[] Prazos = { 1, 12, 24, 168, 720, 0 };
    public static readonly string[] NomesPrazos = { "1 hora", "12 horas", "1 dia", "7 dias", "30 dias", "Nunca" };

    // Acoes que tem atalho
    public const int AbrirHistorico = 0, AbrirEmotes = 1, PrintJanela = 2, PrintTela = 3, RecortarTela = 4,
                     AbrirGaleria = 5, SalvarClipe = 6, AlternarReplay = 7, Gravar = 8;

    public static readonly string[] NomesAcoes =
    {
        "Abrir a \u00e1rea de transfer\u00eancia",
        "Abrir os emotes",
        "Print da janela ativa",
        "Print da tela inteira",
        "Recortar parte da tela",
        "Galeria de prints e clipes",
        "Salvar clipe (replay)",
        "Ligar ou desligar o replay",
        "Iniciar ou parar a gravação"
    };

    public static readonly Atalho[] Padrao =
    {
        new Atalho(Atalho.Win, 0x56),                 // Win + V
        new Atalho(Atalho.Win, 0xBE),                 // Win + .
        new Atalho(Atalho.Ctrl | Atalho.Alt, 0x4A),   // Ctrl + Alt + J
        new Atalho(Atalho.Ctrl | Atalho.Alt, 0x4B),   // Ctrl + Alt + K
        new Atalho(Atalho.Win | Atalho.Shift, 0x53),  // Win + Shift + S
        new Atalho(Atalho.Alt, 0x5A),                 // Alt + Z
        new Atalho(Atalho.Alt, 0x79),                 // Alt + F10
        new Atalho(Atalho.Alt | Atalho.Shift, 0x79),  // Alt + Shift + F10
        new Atalho(Atalho.Alt, 0x78)                  // Alt + F9
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
        if (solta || IsDisposed) return;
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

    // A janela some primeiro: soltar a foto antes deixava um WM_PAINT atrasado
    // desenhar com ela ja solta ("Parametro invalido" no log, 09/11)
    bool solta;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && !solta)
        {
            solta = true;
            foto.Dispose();
            escura.Dispose();
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
        Text            = Config.Nome;

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
        pg.Controls.Add(LinhaPasta(24, ref y, "Clipes: ", ConfigClipe.Pasta));
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
    const string NomeIniciar  = "TrajadoTools";
    const string NomeAntigo   = "ClipHistorico";
    static readonly string TextoBandeja = Config.Nome;

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

        // Replay instantaneo: comeca a gravar em fundo, se estiver ligado
        ConfigClipe.Carregar();
        Gravacao.Recuperar();
        Replay.Preparar(contexto);
        MigrarIniciar();

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

        ToolStripMenuItem galeria = new ToolStripMenuItem("Galeria de prints e clipes");
        galeria.Click += delegate { AlternarGaleria(); };

        ToolStripMenuItem salvarClipe = new ToolStripMenuItem("Salvar clipe agora");
        salvarClipe.Click += delegate { Replay.SalvarClipe(); };

        ToolStripMenuItem replay = new ToolStripMenuItem("Replay instantâneo");
        replay.Click += delegate { Replay.DefinirLigado(!ConfigClipe.Ligado); };

        ToolStripMenuItem gravar = new ToolStripMenuItem("Iniciar gravação");
        gravar.Click += delegate { Gravacao.Alternar(); };
        menu.Opening += delegate
        {
            replay.Checked = ConfigClipe.Ligado;
            gravar.Text = Gravacao.Ativa ? "Parar gravação (" + Gravacao.Tempo + ")" : "Iniciar gravação";
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
            abrir, salvos, emotes, recortar, config, new ToolStripSeparator(),
            galeria, salvarClipe, gravar, replay, new ToolStripSeparator(),
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
            case Config.AbrirGaleria:   AlternarGaleria(); break;
            case Config.SalvarClipe:    Replay.SalvarClipe(); break;
            case Config.AlternarReplay: Replay.DefinirLigado(!ConfigClipe.Ligado); break;
            case Config.Gravar:         Gravacao.Alternar(); break;
        }
    }

    // ---- Galeria (Alt + Z) ----
    void AlternarGaleria()
    {
        if (Galeria.Atual != null) { Galeria.Atual.Close(); return; }
        if (janela != null) janela.Fechar();
        try { new Galeria(nucleo).Mostrar(); }
        catch (Exception ex) { Log("Erro ao abrir a galeria: " + ex); }
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

    // Troca a entrada antiga (ClipHistorico) pela nova e aponta para o .exe
    // atual, que mudou de nome e de pasta
    static void MigrarIniciar()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ChaveIniciar, true))
            {
                if (k == null) return;
                bool ligado = k.GetValue(NomeAntigo) != null || k.GetValue(NomeIniciar) != null;
                k.DeleteValue(NomeAntigo, false);
                if (ligado) k.SetValue(NomeIniciar, "\"" + Application.ExecutablePath + "\"");
            }
        }
        catch (Exception ex) { Log("Erro ao atualizar a inicializacao: " + ex.Message); }
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
        if (Galeria.Atual != null) Galeria.Atual.Close();
        Replay.Parar();
        Gravacao.ParaSair();
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
// 9b. CLIPES (REPLAY INSTANTANEO) E GALERIA
//
// Como o replay da NVIDIA: o ffmpeg grava a tela o tempo todo em pedacos de
// 1 segundo, num anel de arquivos que se sobrescrevem (ocupa so a duracao
// escolhida e uma folga). Salvar emenda os ultimos pedacos byte a byte (o
// formato .ts aceita isso) e copia para .mp4 sem recodificar: leva menos de
// 1 segundo e nao pesa no jogo.
//
// A tela e capturada e codificada na placa de video (ddagrab + NVENC), sem
// passar pelo processador. So reduzir a resolucao traz a imagem para o
// processador: o redimensionamento na placa (scale_d3d11) falha com a imagem
// do ddagrab no ffmpeg 9.
//
// O ffmpeg nao grava o som do PC no Windows. O som e lido aqui (WASAPI,
// loopback e microfone) e entregue por pipes, preso ao relogio do Windows
// para nao sair do compasso do video.
// ---------------------------------------------------------------------
static class ConfigClipe
{
    public static bool   Ligado        = true;
    public static int    Segundos      = 30;
    public static int    Altura        = 0;        // 0 = a do monitor; senao, o lado menor do video
    public static int    Fps           = 60;
    public static int    Mbps          = 50;
    public static string Codec         = "h264";
    public static string Monitor       = "";       // "" = principal; senao o nome do Windows (\\.\DISPLAY2)
    public static bool   SomPc         = true;
    public static bool   Microfone     = false;
    public static bool   MostrarCursor = true;
    public static string Pasta         = PastaPadrao;

    public static string PastaPadrao
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "CLIPES"); }
    }

    public static readonly string Local = Config.PastaDoApp(Environment.SpecialFolder.LocalApplicationData);
    public static readonly string PastaAnel       = Path.Combine(Local, "replay");
    public static readonly string PastaMiniaturas = Path.Combine(Local, "miniaturas");

    public static readonly int[] Duracoes = { 15, 30, 60, 120, 180, 300, 600 };
    public static readonly int[] Alturas  = { 0, 1440, 1080, 720, 480 };
    public static readonly int[] Quadros  = { 30, 60, 120, 144 };
    public static readonly string[] Codecs      = { "h264", "hevc", "av1" };
    public static readonly string[] NomesCodecs = { "H.264", "HEVC (H.265)", "AV1" };
    public const int MbpsMin = 5, MbpsMax = 130;

    // Predefinicoes de qualidade: resolucao, quadros e Mbps
    public static readonly string[] NomesQualidade = { "Baixa", "Média", "Alta", "Personalizada" };
    static readonly int[,] qualidades = { { 720, 30, 10 }, { 1080, 60, 20 }, { 0, 60, 50 } };

    public static int Qualidade()
    {
        for (int i = 0; i < 3; i++)
            if (qualidades[i, 0] == Altura && qualidades[i, 1] == Fps && qualidades[i, 2] == Mbps) return i;
        return 3;
    }

    public static void UsarQualidade(int i)
    {
        if (i < 0 || i > 2) return;
        Altura = qualidades[i, 0];
        Fps    = qualidades[i, 1];
        Mbps   = qualidades[i, 2];
    }

    public static string NomeDuracao(int s) { return s < 60 ? s + " s" : (s / 60) + " min"; }
    public static string NomeAltura(int a)  { return a == 0 ? "Nativa" : a + "p"; }

    // Espaco maximo que o anel ocupa no disco, em MB
    public static int EspacoAnel() { return (Segundos + Replay.Folga) * Mbps * 3 / 2 / 8; }

    static string Arquivo { get { return Path.Combine(Config.PastaDados, "clipes.ini"); } }

    public static void Carregar()
    {
        try
        {
            if (!File.Exists(Arquivo)) return;
            foreach (string linha in File.ReadAllLines(Arquivo))
            {
                int igual = linha.IndexOf('=');
                if (igual <= 0) continue;
                string c = linha.Substring(0, igual).Trim(), v = linha.Substring(igual + 1).Trim();
                int n;
                bool num = int.TryParse(v, out n);
                switch (c)
                {
                    case "Ligado":    Ligado = v == "1"; break;
                    case "Segundos":  if (num && n >= 5 && n <= 1200) Segundos = n; break;
                    case "Altura":    if (num && n >= 0) Altura = n; break;
                    case "Fps":       if (num && n >= 10 && n <= 240) Fps = n; break;
                    case "Mbps":      if (num) Mbps = Math.Max(MbpsMin, Math.Min(MbpsMax, n)); break;
                    case "Codec":     if (Array.IndexOf(Codecs, v) >= 0) Codec = v; break;
                    case "Monitor":   Monitor = v; break;
                    case "SomPc":     SomPc = v == "1"; break;
                    case "Microfone": Microfone = v == "1"; break;
                    case "Cursor":    MostrarCursor = v == "1"; break;
                    case "Pasta":     if (v.Length > 0) Pasta = v; break;
                }
            }
        }
        catch (Exception ex) { App.Log("Erro ao ler clipes.ini: " + ex.Message); }
    }

    public static void Salvar()
    {
        try
        {
            Directory.CreateDirectory(Config.PastaDados);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Ligado=" + (Ligado ? "1" : "0"));
            sb.AppendLine("Segundos=" + Segundos);
            sb.AppendLine("Altura=" + Altura);
            sb.AppendLine("Fps=" + Fps);
            sb.AppendLine("Mbps=" + Mbps);
            sb.AppendLine("Codec=" + Codec);
            sb.AppendLine("Monitor=" + Monitor);
            sb.AppendLine("SomPc=" + (SomPc ? "1" : "0"));
            sb.AppendLine("Microfone=" + (Microfone ? "1" : "0"));
            sb.AppendLine("Cursor=" + (MostrarCursor ? "1" : "0"));
            sb.AppendLine("Pasta=" + Pasta);
            File.WriteAllText(Arquivo, sb.ToString());
        }
        catch (Exception ex) { App.Log("Erro ao salvar clipes.ini: " + ex.Message); }
    }
}

// Onde esta o ffmpeg, rodar e baixar
static class Ffmpeg
{
    public static readonly string PastaPropria = Path.Combine(ConfigClipe.Local, "ffmpeg");
    const string Endereco = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    static string achado;

    public static string Caminho
    {
        get
        {
            if (achado != null && File.Exists(achado)) return achado;
            achado = Procurar();
            return achado;
        }
    }

    static string Procurar()
    {
        List<string> c = new List<string>();
        string app = Path.GetDirectoryName(Application.ExecutablePath);
        c.Add(Path.Combine(app, "ffmpeg.exe"));
        c.Add(Path.Combine(app, "ffmpeg", "ffmpeg.exe"));
        c.Add(Path.Combine(PastaPropria, "ffmpeg.exe"));
        c.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ferramentas", "ffmpeg", "bin", "ffmpeg.exe"));
        foreach (string p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            if (p.Trim().Length > 0)
                try { c.Add(Path.Combine(p.Trim().Trim('"'), "ffmpeg.exe")); } catch { }
        foreach (string f in c)
            if (File.Exists(f)) return f;
        return null;
    }

    public static string Aspas(string s) { return "\"" + s + "\""; }

    public static ProcessStartInfo Info(string args, string pasta)
    {
        ProcessStartInfo i = new ProcessStartInfo(Caminho, args);
        i.UseShellExecute = false;
        i.CreateNoWindow = true;
        i.RedirectStandardError = true;
        i.StandardErrorEncoding = Encoding.UTF8;
        if (pasta != null) i.WorkingDirectory = pasta;
        return i;
    }

    // Para gravacoes longas: o ffmpeg conta os quadros a cada segundo na
    // saida padrao ("-progress pipe:1"). Se a conta parar, a captura travou
    // sem o ffmpeg fechar: aconteceu com o BlueStacks mudando o modo da tela.
    public const string ArgsProgresso = "-progress pipe:1 -stats_period 1 ";

    public static void Vigiar(Process p, Vigia v)
    {
        p.StartInfo.RedirectStandardOutput = true;
        p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
        {
            long n;
            if (e.Data != null && e.Data.StartsWith("frame=") && long.TryParse(e.Data.Substring(6), out n)) v.Quadro(n);
        };
    }

    // Roda e espera. Devolve o codigo de saida (-1 = passou do tempo, -2 = sem ffmpeg).
    public static int Rodar(string args, string pasta, int msMax, out string saida)
    {
        saida = "";
        if (Caminho == null) return -2;
        using (Process p = new Process())
        {
            StringBuilder sb = new StringBuilder();
            p.StartInfo = Info(args, pasta);
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data != null) lock (sb) if (sb.Length < 20000) sb.AppendLine(e.Data);
            };
            p.Start();
            Trabalho.Prender(p);
            p.BeginErrorReadLine();
            bool terminou = p.WaitForExit(msMax);
            if (!terminou) { try { p.Kill(); } catch { } }
            p.WaitForExit(3000);
            lock (sb) saida = sb.ToString();
            return terminou ? p.ExitCode : -1;
        }
    }

    // Ultima linha util do que o ffmpeg escreveu, sem o "[ddagrab @ 0x...]"
    public static string Resumo(string saida)
    {
        if (string.IsNullOrEmpty(saida)) return null;
        string[] linhas = saida.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = linhas.Length - 1; i >= 0; i--)
        {
            string l = linhas[i].Trim();
            while (l.StartsWith("[") && l.IndexOf(']') > 0) l = l.Substring(l.IndexOf(']') + 1).Trim();
            if (l.Length == 0 || l.StartsWith("Exiting") || l.StartsWith("Conversion failed")) continue;
            return l.Length > 140 ? l.Substring(0, 140) + "..." : l;
        }
        return null;
    }

    // Baixa o pacote oficial (gyan.dev) e tira dele so o ffmpeg.exe.
    // Os avisos chegam numa thread de fundo; fim(null) = deu certo.
    public static void Baixar(Action<int> progresso, Action<string> fim)
    {
        Thread t = new Thread(delegate()
        {
            string zip = Path.Combine(Path.GetTempPath(), "ClipHistorico-ffmpeg.zip");
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;   // TLS 1.2
                System.Net.HttpWebRequest r = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(Endereco);
                r.UserAgent = "ClipHistorico";
                using (System.Net.WebResponse resp = r.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (FileStream f = File.Create(zip))
                {
                    long total = resp.ContentLength, lido = 0;
                    byte[] buf = new byte[81920];
                    int n, ultimo = -1;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        f.Write(buf, 0, n);
                        lido += n;
                        int pct = total > 0 ? (int)(lido * 100 / total) : 0;
                        if (pct != ultimo) { ultimo = pct; progresso(pct); }
                    }
                }

                Directory.CreateDirectory(PastaPropria);
                string destino = Path.Combine(PastaPropria, "ffmpeg.exe");
                bool achou = false;
                using (FileStream fz = File.OpenRead(zip))
                using (ZipArchive z = new ZipArchive(fz, ZipArchiveMode.Read))
                    foreach (ZipArchiveEntry e in z.Entries)
                    {
                        if (!e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase)) continue;
                        using (Stream es = e.Open())
                        using (FileStream fo = File.Create(destino + ".novo"))
                            es.CopyTo(fo);
                        achou = true;
                        break;
                    }
                if (!achou) throw new Exception("o ffmpeg.exe não veio no pacote");
                if (File.Exists(destino)) File.Delete(destino);
                File.Move(destino + ".novo", destino);
                achado = null;
                fim(null);
            }
            catch (Exception ex)
            {
                App.Log("Erro ao baixar o ffmpeg: " + ex.Message);
                fim(ex.Message);
            }
            finally { try { File.Delete(zip); } catch { } }
        });
        t.IsBackground = true;
        t.Start();
    }
}

// Ultima vez que a captura andou (thread-safe)
class Vigia
{
    static readonly Stopwatch relogio = Stopwatch.StartNew();
    long quadros = -1, quando = relogio.ElapsedMilliseconds;

    public void Quadro(long n)
    {
        if (n != Interlocked.Read(ref quadros))
        {
            Interlocked.Exchange(ref quadros, n);
            Interlocked.Exchange(ref quando, relogio.ElapsedMilliseconds);
        }
    }

    // ms desde o ultimo quadro novo (conta desde a criacao, para dar tempo de iniciar)
    public long Parado { get { return relogio.ElapsedMilliseconds - Interlocked.Read(ref quando); } }
    public long Quadros { get { return Interlocked.Read(ref quadros); } }

    // O Windows tirou a tela do ddagrab (erro 887a0026, DXGI_ERROR_ACCESS_LOST).
    // Acontece quando um jogo em tela cheia ganha ou perde o foco: o BlueStacks
    // com Vulkan faz isso a cada Alt+Tab. O ffmpeg nao fecha (segue gravando so
    // o som), entao a linha de erro e o aviso mais rapido que existe.
    public volatile bool TelaPerdida;

    public static bool PerdeuATela(string linha)
    {
        return linha != null && (linha.Contains("AcquireNextFrame failed") || linha.Contains("Error during demuxing"));
    }
}

// Se o programa fechar ou cair, o Windows fecha o ffmpeg junto
static class Trabalho
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr seguranca, string nome);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int classe, ref Limites info, int tamanho);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr processo);

    [StructLayout(LayoutKind.Sequential)]
    struct Limites
    {
        public long TempoProcesso, TempoTrabalho;
        public uint Flags;
        public UIntPtr MemMin, MemMax;
        public uint MaxProcessos;
        public UIntPtr Afinidade;
        public uint Prioridade, Agendamento;
        public ulong Io1, Io2, Io3, Io4, Io5, Io6;
        public UIntPtr MemProcesso, MemTrabalho, PicoProcesso, PicoTrabalho;
    }

    static IntPtr job;

    public static void Prender(Process p)
    {
        try
        {
            if (job == IntPtr.Zero)
            {
                IntPtr j = CreateJobObject(IntPtr.Zero, null);
                Limites l = new Limites();
                l.Flags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                SetInformationJobObject(j, 9, ref l, Marshal.SizeOf(typeof(Limites)));
                job = j;
            }
            AssignProcessToJobObject(job, p.Handle);
        }
        catch { }
    }
}

// Numero do monitor para o ddagrab: a ordem do DXGI nao e a do Windows
static class Dxgi
{
    [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr fabrica);
    delegate int Enumerar(IntPtr eu, uint indice, out IntPtr saida);
    delegate int Descrever(IntPtr eu, IntPtr desc);

    static T Metodo<T>(IntPtr obj, int posicao) where T : class
    {
        IntPtr tabela = Marshal.ReadIntPtr(obj);
        return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(tabela, posicao * IntPtr.Size), typeof(T));
    }

    // -1 = nao achou
    public static int IndiceDoMonitor(string nome)
    {
        try
        {
            Guid g = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            IntPtr f;
            if (CreateDXGIFactory1(ref g, out f) != 0) return -1;
            try
            {
                IntPtr a;
                if (Metodo<Enumerar>(f, 7)(f, 0, out a) != 0) return -1;   // EnumAdapters
                try
                {
                    for (uint i = 0; i < 16; i++)
                    {
                        IntPtr o;
                        if (Metodo<Enumerar>(a, 7)(a, i, out o) != 0) break;   // EnumOutputs
                        IntPtr d = Marshal.AllocHGlobal(128);
                        try
                        {
                            if (Metodo<Descrever>(o, 7)(o, d) == 0 &&                 // GetDesc
                                string.Equals(Marshal.PtrToStringUni(d), nome, StringComparison.OrdinalIgnoreCase))
                                return (int)i;
                        }
                        finally { Marshal.FreeHGlobal(d); Marshal.Release(o); }
                    }
                }
                finally { Marshal.Release(a); }
            }
            finally { Marshal.Release(f); }
        }
        catch (Exception ex) { App.Log("Erro ao listar monitores: " + ex.Message); }
        return -1;
    }
}

// ---- Som do PC e microfone (WASAPI) ----
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class EnumeradorDeSomCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEnumeradorDeSom
{
    [PreserveSig] int EnumAudioEndpoints(int fluxo, int estados, out IntPtr lista);
    [PreserveSig] int GetDefaultAudioEndpoint(int fluxo, int papel, out IAparelhoDeSom aparelho);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAparelhoDeSom
{
    [PreserveSig] int Activate(ref Guid iid, int contexto, IntPtr parametros, [MarshalAs(UnmanagedType.IUnknown)] out object objeto);
}

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IClienteDeSom
{
    [PreserveSig] int Initialize(int modo, int flags, long duracao, long periodo, IntPtr formato, IntPtr sessao);
    [PreserveSig] int GetBufferSize(out uint quadros);
    [PreserveSig] int GetStreamLatency(out long latencia);
    [PreserveSig] int GetCurrentPadding(out uint quadros);
    [PreserveSig] int IsFormatSupported(int modo, IntPtr formato, out IntPtr parecido);
    [PreserveSig] int GetMixFormat(out IntPtr formato);
    [PreserveSig] int GetDevicePeriod(out long padrao, out long minimo);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr evento);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object servico);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ICapturaDeSom
{
    [PreserveSig] int GetBuffer(out IntPtr dados, out uint quadros, out uint flags, out long posicao, out long qpc);
    [PreserveSig] int ReleaseBuffer(uint quadros);
    [PreserveSig] int GetNextPacketSize(out uint quadros);
}

// Le o som e entrega ao ffmpeg por um pipe. O som do PC so chega quando algo
// toca: no silencio, completa com zeros pelo relogio do Windows, senao o
// audio encolheria e sairia do compasso do video. Se o aparelho adiantar,
// descarta o excesso; se atrasar, completa.
class SomCaptura : IDisposable
{
    public readonly bool Microfone;
    public readonly string NomePipe;
    public int Taxa, Canais, BytesPorQuadro;
    public string Formato;   // f32le, s16le... (null = nao abriu)
    public string Erro;

    readonly ManualResetEvent pronto = new ManualResetEvent(false);
    readonly System.IO.Pipes.NamedPipeServerStream pipe;
    readonly Thread thread;
    volatile bool parar;

    public SomCaptura(bool microfone, string nomePipe)
    {
        Microfone = microfone;
        NomePipe = nomePipe;
        pipe = new System.IO.Pipes.NamedPipeServerStream(nomePipe, System.IO.Pipes.PipeDirection.Out, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous, 0, 1 << 16);
        thread = new Thread(Laco, 256 * 1024);
        thread.IsBackground = true;
        thread.Priority = ThreadPriority.Highest;
        thread.Start();
    }

    // false = sem som (o replay segue, mudo)
    public bool Esperar(int ms) { return pronto.WaitOne(ms) && Formato != null; }

    public string Entrada
    {
        get
        {
            return "-thread_queue_size 4096 -f " + Formato + " -ar " + Taxa + " -ac " + Canais +
                   " -i \\\\.\\pipe\\" + NomePipe;
        }
    }

    void Laco()
    {
        IClienteDeSom cliente = null;
        ICapturaDeSom cap = null;
        try
        {
            try
            {
                IEnumeradorDeSom en = (IEnumeradorDeSom)new EnumeradorDeSomCom();
                IAparelhoDeSom ap;
                int hr = en.GetDefaultAudioEndpoint(Microfone ? 1 : 0, 0, out ap);
                if (hr != 0 || ap == null) throw new Exception("nenhum aparelho de som (0x" + hr.ToString("X") + ")");
                Guid iid = typeof(IClienteDeSom).GUID;
                object o;
                Marshal.ThrowExceptionForHR(ap.Activate(ref iid, 23, IntPtr.Zero, out o));
                cliente = (IClienteDeSom)o;

                IntPtr fmt;
                Marshal.ThrowExceptionForHR(cliente.GetMixFormat(out fmt));
                try
                {
                    int tag = Marshal.ReadInt16(fmt, 0) & 0xFFFF;
                    Canais = Marshal.ReadInt16(fmt, 2);
                    Taxa = Marshal.ReadInt32(fmt, 4);
                    BytesPorQuadro = Marshal.ReadInt16(fmt, 12);
                    int bits = Marshal.ReadInt16(fmt, 14);
                    bool real = tag == 3;
                    if (tag == 0xFFFE)
                    {
                        byte[] g = new byte[16];
                        Marshal.Copy(new IntPtr(fmt.ToInt64() + 24), g, 0, 16);
                        real = new Guid(g) == new Guid("00000003-0000-0010-8000-00aa00389b71");
                    }
                    Formato = real ? (bits == 64 ? "f64le" : "f32le") : bits == 16 ? "s16le" : bits == 24 ? "s24le" : "s32le";
                    // 1 s de folga no aparelho; loopback = o que sai nas caixas de som
                    Marshal.ThrowExceptionForHR(cliente.Initialize(0, Microfone ? 0 : 0x00020000, 10000000, 0, fmt, IntPtr.Zero));
                }
                finally { Marshal.FreeCoTaskMem(fmt); }

                iid = typeof(ICapturaDeSom).GUID;
                Marshal.ThrowExceptionForHR(cliente.GetService(ref iid, out o));
                cap = (ICapturaDeSom)o;
            }
            catch (Exception ex)
            {
                Erro = ex.Message;
                Formato = null;
                return;
            }
            finally { pronto.Set(); }

            // Espera o ffmpeg abrir o pipe
            IAsyncResult r = pipe.BeginWaitForConnection(null, null);
            while (!r.AsyncWaitHandle.WaitOne(200))
                if (parar) return;
            pipe.EndWaitForConnection(r);

            byte[] buf = new byte[Taxa * BytesPorQuadro / 2];
            byte[] zeros = new byte[Taxa * BytesPorQuadro / 10];   // 100 ms
            long folga = Taxa * 30 / 1000;                          // 30 ms
            cliente.Start();
            Stopwatch relogio = Stopwatch.StartNew();
            long escritos = 0, ultimoPacote = 0;

            while (!parar)
            {
                Thread.Sleep(5);
                uint tam;
                while (!parar && cap.GetNextPacketSize(out tam) == 0 && tam > 0)
                {
                    IntPtr dados;
                    uint q, flags;
                    long pos, qpc;
                    if (cap.GetBuffer(out dados, out q, out flags, out pos, out qpc) != 0) break;
                    int bytes = (int)q * BytesPorQuadro;
                    if (bytes > buf.Length) buf = new byte[bytes];
                    if ((flags & 2) != 0) Array.Clear(buf, 0, bytes);   // AUDCLNT_BUFFERFLAGS_SILENT
                    else Marshal.Copy(dados, buf, 0, bytes);
                    cap.ReleaseBuffer(q);

                    long esperado = relogio.ElapsedTicks * Taxa / Stopwatch.Frequency;
                    long pular = Math.Max(0, Math.Min(q, escritos + q - esperado - 2 * folga));
                    if (q - pular > 0)
                    {
                        pipe.Write(buf, (int)pular * BytesPorQuadro, (int)(q - pular) * BytesPorQuadro);
                        escritos += q - pular;
                    }
                    ultimoPacote = relogio.ElapsedMilliseconds;
                }

                long falta = relogio.ElapsedTicks * Taxa / Stopwatch.Frequency - escritos;
                if (falta > folga && (relogio.ElapsedMilliseconds - ultimoPacote > 40 || falta > 4 * folga))
                {
                    while (falta > 0 && !parar)
                    {
                        int qz = (int)Math.Min(falta, zeros.Length / BytesPorQuadro);
                        pipe.Write(zeros, 0, qz * BytesPorQuadro);
                        escritos += qz;
                        falta -= qz;
                    }
                }
            }
        }
        catch (IOException) { }                 // o ffmpeg fechou
        catch (ObjectDisposedException) { }     // parou no meio de uma escrita
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Log("Erro na captura de som: " + ex.Message); }
        finally
        {
            try { if (cliente != null) cliente.Stop(); } catch { }
            try { if (cap != null) Marshal.ReleaseComObject(cap); } catch { }
            try { if (cliente != null) Marshal.ReleaseComObject(cliente); } catch { }
        }
    }

    public void Dispose()
    {
        parar = true;
        try { pipe.Dispose(); } catch { }
        thread.Join(1500);
        pronto.Close();
    }
}

// O replay em si: liga, desliga, se recupera sozinho e salva clipes
static class Replay
{
    public const int Folga = 8;   // pedacos a mais no anel: salvar nunca le um pedaco sendo sobrescrito

    public class Opcoes
    {
        public int Fps, Altura, Mbps, Segundos, Monitor;
        public bool MostrarCursor;
        public string Codec, Codificador;
    }

    public static event Action Mudou;   // sempre na thread da janela
    public static bool Gravando, Iniciando, Salvando;
    public static string Problema;      // ultima falha
    public static string Codificador;   // em uso (h264_nvenc...)
    public static int SegundosAtuais;
    public static int ProgressoDownload = -1;

    static SynchronizationContext ui;
    static Timer espera, reconfigurar, vigiar;
    static Vigia vigia;
    static DateTime ultimoTravou;
    public const int TravadoMs = 2500;   // sem quadro novo por esse tempo = captura travada (o ffmpeg conta a cada 1 s)

    // O anel sobrevive aos reinicios: cada ffmpeg (sessao) grava pedacos com o
    // proprio nome e o relogio continua de onde o anterior parou
    // (-output_ts_offset). Assim o clipe emenda as sessoes com um salto de
    // menos de 1 s no lugar da queda, em vez de perder tudo o que tinha.
    // So comeca do zero quando o formato do video muda (monitor, resolucao...).
    static readonly Stopwatch relogioAnel = new Stopwatch();
    static readonly object travaAnel = new object();
    static string assinaturaAnel;
    static int sessaoAtual = -1;
    static DateTime ultimaLimpeza;
    static Process proc, morrendo;
    static SomCaptura somPc, somMic;
    static int geracao, falhas, anel;
    static DateTime inicio;
    static string descricao = "";
    static bool testando;
    static readonly Dictionary<string, bool> testados = new Dictionary<string, bool>();
    static readonly List<string> linhas = new List<string>();

    public static void Preparar(SynchronizationContext c)
    {
        ui = c;
        espera = new Timer();
        espera.Tick += delegate { espera.Stop(); Iniciar(); };
        reconfigurar = new Timer();
        reconfigurar.Interval = 900;
        reconfigurar.Tick += delegate
        {
            reconfigurar.Stop();
            if (ConfigClipe.Ligado) { falhas = 0; Iniciar(); }
        };
        vigiar = new Timer();
        vigiar.Interval = 1000;
        vigiar.Tick += delegate
        {
            if (Gravando && vigia != null && (vigia.TelaPerdida || vigia.Parado > TravadoMs)) Travou();
            Gravacao.Vigiar();
            if ((DateTime.Now - ultimaLimpeza).TotalSeconds >= 5)
            {
                ultimaLimpeza = DateTime.Now;
                int seg = SegundosAtuais;
                ThreadPool.QueueUserWorkItem(delegate { LimparAnel(ConfigClipe.PastaAnel, seg, false); });
            }
        };
        vigiar.Start();
        if (ConfigClipe.Ligado) Iniciar();
    }

    // A captura parou sem o ffmpeg fechar: reinicia na hora. O anel continua.
    static bool Travou()
    {
        string saida;
        lock (linhas) saida = string.Join("\n", linhas.ToArray());
        bool perdida = vigia != null && vigia.TelaPerdida;
        App.Log("Replay " + (perdida ? "perdeu a tela" : "travou") + " (" + (vigia == null ? -1 : vigia.Quadros) + " quadros): " + saida);
        bool rapida = (DateTime.Now - inicio).TotalSeconds < 3;   // caiu logo depois de abrir: nao insiste sem pausa
        Parar();
        // Perder a tela e normal (Alt+Tab num jogo em tela cheia): volta calado
        if (!perdida)
        {
            Problema = "a captura da tela travou e foi reiniciada";
            if ((DateTime.Now - ultimoTravou).TotalSeconds > 60)
                Aviso.Mostrar("Replay reiniciado", "A captura da tela travou (um jogo mudou o modo da tela?)", Tema.Suave);
            ultimoTravou = DateTime.Now;
        }
        if (espera != null) { espera.Interval = rapida ? 1000 : 30; espera.Start(); }
        Avisar();
        return true;
    }

#if TESTE
    // Faz de conta que o ffmpeg escreveu o erro do ddagrab (887a0026)
    public static void SimularPerdaDaTela()
    {
        Vigia v = vigia;
        if (v == null) return;
        v.TelaPerdida = true;
        NaJanela(delegate { if (Gravando && vigia == v) Travou(); });
    }
#endif

    static void Avisar() { if (Mudou != null) Mudou(); }
    public static void Notificar() { Avisar(); }   // a gravacao tambem avisa a galeria

    public static void NaJanela(SendOrPostCallback f)
    {
        try { if (ui != null) ui.Post(f, null); } catch { }
    }

    // Configuracao atual, lida na thread da janela
    public static Opcoes OpcoesAtuais()
    {
        Opcoes o = new Opcoes();
        o.Fps = ConfigClipe.Fps;
        o.Altura = ConfigClipe.Altura;
        o.Mbps = ConfigClipe.Mbps;
        o.Segundos = ConfigClipe.Segundos;
        o.MostrarCursor = ConfigClipe.MostrarCursor;
        o.Codec = ConfigClipe.Codec;
        return o;
    }

    // null = sem som (o video segue, mudo)
    public static SomCaptura AbrirSom(bool microfone, string nome)
    {
        SomCaptura s = new SomCaptura(microfone, nome);
        if (s.Esperar(3000)) return s;
        App.Log((microfone ? "Microfone" : "Som do PC") + " indisponível: " + s.Erro);
        s.Dispose();
        return null;
    }

    public static string StatusLongo
    {
        get
        {
            if (!ConfigClipe.Ligado) return "desligado";
            if (Gravando) return "gravando os últimos " + ConfigClipe.NomeDuracao(SegundosAtuais) + Tema.Ponto + descricao;
            if (Iniciando) return "iniciando...";
            return Problema != null ? "parou: " + Problema : "parado";
        }
    }

    public static void DefinirLigado(bool ligar)
    {
        if (ConfigClipe.Ligado == ligar) return;
        ConfigClipe.Ligado = ligar;
        ConfigClipe.Salvar();
        if (ligar)
        {
            falhas = 0;
            Problema = null;
            Iniciar();
            Aviso.Mostrar("Replay instantâneo ligado",
                Config.Atalhos[Config.SalvarClipe] + " salva os últimos " + ConfigClipe.NomeDuracao(ConfigClipe.Segundos), Tema.Sucesso);
        }
        else
        {
            Parar();
            Aviso.Mostrar("Replay instantâneo desligado", "Nada está sendo gravado", Tema.Suave);
        }
        Avisar();
    }

    // Configuracao mudou: reinicia depois de uma pausa (varios cliques = um reinicio)
    public static void Reconfigurar()
    {
        if (!ConfigClipe.Ligado || reconfigurar == null) return;
        reconfigurar.Stop();
        reconfigurar.Start();
    }

    public static void Parar()
    {
        geracao++;
        if (espera != null) espera.Stop();
        if (proc != null)
        {
            try { if (!proc.HasExited) proc.Kill(); } catch { }
            morrendo = proc;
            proc = null;
        }
        vigia = null;
        Soltar(ref somPc);
        Soltar(ref somMic);
        Gravando = false;
        Iniciando = false;
        Avisar();
    }

    static void Soltar(ref SomCaptura s)
    {
        if (s != null) { s.Dispose(); s = null; }
    }

    public static Screen MonitorEscolhido()
    {
        foreach (Screen s in Screen.AllScreens)
            if (s.DeviceName == ConfigClipe.Monitor) return s;
        return Screen.PrimaryScreen;
    }

    static void Iniciar()
    {
        if (!ConfigClipe.Ligado) return;
        Parar();
        int g = geracao;
        Iniciando = true;
        Problema = null;
        Avisar();

        // A configuracao e lida aqui, na thread da janela
        Opcoes o = OpcoesAtuais();
        Screen tela = MonitorEscolhido();
        bool pc = ConfigClipe.SomPc, mic = ConfigClipe.Microfone;
        Process velho = morrendo;
        morrendo = null;
        int sessaoVelha = sessaoAtual, anelVelho = anel;

        Thread t = new Thread(delegate() { Preparo(g, o, tela, pc, mic, velho, sessaoVelha, anelVelho); });
        t.IsBackground = true;
        t.Start();
    }

    static void Preparo(int g, Opcoes o, Screen tela, bool comPc, bool comMic, Process velho, int sessaoVelha, int anelVelho)
    {
        SomCaptura pc = null, mic = null;
        try
        {
            bool fechou = true;
            if (velho != null) { try { fechou = velho.WaitForExit(3000); velho.Dispose(); } catch { } }
            if (fechou) lock (travaAnel) FecharSessao(ConfigClipe.PastaAnel, sessaoVelha, anelVelho);
            if (Ffmpeg.Caminho == null) { Falhou(g, "falta o ffmpeg, que grava a tela (baixe em Configurações)", false, true); return; }

            o.Monitor = Math.Max(0, Dxgi.IndiceDoMonitor(tela.DeviceName));
            o.Codificador = Escolher(o.Codec, o.Monitor);
            if (o.Codificador == null)
            {
                Falhou(g, "a placa de vídeo não aceitou " + NomeCodec(o.Codec) + "; escolha outro formato", false, false);
                return;
            }

            Directory.CreateDirectory(ConfigClipe.PastaAnel);

            int pid = Process.GetCurrentProcess().Id;
            if (comPc) pc = AbrirSom(false, "TrajadoTools-pc-" + pid + "-" + g);
            if (comMic) mic = AbrirSom(true, "TrajadoTools-mic-" + pid + "-" + g);

            // Mesmo formato de video e de som = a sessao nova emenda na anterior
            string assinatura = tela.Bounds.Width + "x" + tela.Bounds.Height + "|" + o.Monitor + "|" + o.Altura + "|" + o.Fps + "|" +
                                o.Codificador + "|" + (pc != null) + "|" + (mic != null);
            double deslocamento;
            lock (travaAnel)
            {
                if (assinatura != assinaturaAnel)
                {
                    foreach (string f in Directory.GetFiles(ConfigClipe.PastaAnel))
                        try { File.Delete(f); } catch { }
                    assinaturaAnel = assinatura;
                    relogioAnel.Restart();
                }
                else LimparAnel(ConfigClipe.PastaAnel, o.Segundos, true);
                deslocamento = relogioAnel.Elapsed.TotalSeconds;
            }

            string args = Ffmpeg.ArgsProgresso + MontarArgs(o, pc == null ? null : pc.Entrada, mic == null ? null : mic.Entrada, g, deslocamento);
            lock (linhas) linhas.Clear();
            Process p = new Process();
            p.StartInfo = Ffmpeg.Info(args, ConfigClipe.PastaAnel);
            Vigia v = new Vigia();
            Ffmpeg.Vigiar(p, v);
            p.EnableRaisingEvents = true;
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                lock (linhas) { linhas.Add(e.Data); if (linhas.Count > 12) linhas.RemoveAt(0); }
                if (!v.TelaPerdida && Vigia.PerdeuATela(e.Data))
                {
                    v.TelaPerdida = true;
                    NaJanela(delegate { if (Gravando && vigia == v) Travou(); });
                }
            };
            p.Exited += delegate { NaJanela(delegate { Terminou(g, p); }); };
            p.Start();
            Trabalho.Prender(p);
            try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();

            Rectangle b = tela.Bounds;
            string desc = (o.Altura == 0 ? b.Width + " × " + b.Height : o.Altura + "p") + Tema.Ponto + o.Fps + " FPS" +
                          Tema.Ponto + o.Mbps + " Mbps" + Tema.Ponto + NomeCodificador(o.Codificador) +
                          (pc == null && mic == null ? Tema.Ponto + "sem som" : "");
            SomCaptura pcF = pc, micF = mic;
            pc = null;
            mic = null;
            NaJanela(delegate
            {
                if (g != geracao)
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    if (pcF != null) pcF.Dispose();
                    if (micF != null) micF.Dispose();
                    return;
                }
                proc = p;
                vigia = v;
                somPc = pcF;
                somMic = micF;
                Codificador = o.Codificador;
                SegundosAtuais = o.Segundos;
                anel = o.Segundos + Folga;
                sessaoAtual = g;
                descricao = desc;
                Iniciando = false;
                if (p.HasExited) { Terminou(g, p); return; }
                Gravando = true;
                inicio = DateTime.Now;
                Avisar();
                if (v.TelaPerdida) Travou();   // perdeu a tela antes de chegar aqui
            });
        }
        catch (Exception ex)
        {
            if (pc != null) pc.Dispose();
            if (mic != null) mic.Dispose();
            App.Log("Erro ao iniciar o replay: " + ex);
            Falhou(g, ex.Message, true, false);
        }
    }

    static void Terminou(int g, Process p)
    {
        if (g != geracao || proc != p) return;
        // Sem som, o video e a unica entrada e o ffmpeg fecha ao perder a tela: volta na hora
        if (vigia != null && vigia.TelaPerdida) { Travou(); return; }
        string saida;
        lock (linhas) saida = string.Join("\n", linhas.ToArray());
        int codigo = 0;
        try { codigo = p.ExitCode; } catch { }
        string motivo = Ffmpeg.Resumo(saida) ?? "o ffmpeg fechou (código " + codigo + ")";
        App.Log("Replay parou (" + codigo + "): " + saida);
        bool durou = Gravando && (DateTime.Now - inicio).TotalSeconds > 90;
        Parar();
        Caiu(motivo, true, false, durou);
    }

    static void Falhou(int g, string motivo, bool tentarDeNovo, bool calado)
    {
        NaJanela(delegate
        {
            if (g != geracao) return;
            Iniciando = false;
            Caiu(motivo, tentarDeNovo, calado, false);
        });
    }

    // Tenta de novo em 4, 8, 16 e depois a cada 30 s; avisa na tela uma vez
    static void Caiu(string motivo, bool tentarDeNovo, bool calado, bool durou)
    {
        Gravando = false;
        Iniciando = false;
        if (durou) falhas = 0;
        falhas++;
        Problema = motivo;
        if (!calado && (falhas == 3 || (!tentarDeNovo && falhas == 1)))
            Aviso.Mostrar("O replay instantâneo parou", motivo, Tema.Perigo);
        if (tentarDeNovo && ConfigClipe.Ligado && espera != null)
        {
            espera.Interval = Math.Min(30, 2 << Math.Min(falhas, 4)) * 1000;
            espera.Start();
        }
        Avisar();
    }

    // ---- Codificadores ----
    static string[] Candidatos(string codec)
    {
        if (codec == "hevc") return new[] { "hevc_nvenc", "hevc_amf", "hevc_qsv" };
        if (codec == "av1")  return new[] { "av1_nvenc", "av1_amf", "av1_qsv" };
        return new[] { "h264_nvenc", "h264_amf", "h264_qsv", "libx264" };
    }

    public static string NomeCodec(string codec)
    {
        int i = Array.IndexOf(ConfigClipe.Codecs, codec);
        return i >= 0 ? ConfigClipe.NomesCodecs[i] : codec;
    }

    public static string NomeCodificador(string enc)
    {
        if (enc == null) return "";
        if (enc.EndsWith("_nvenc")) return "NVIDIA NVENC (placa de vídeo)";
        if (enc.EndsWith("_amf"))   return "AMD (placa de vídeo)";
        if (enc.EndsWith("_qsv"))   return "Intel Quick Sync";
        return "processador (x264)";
    }

    public static string Escolher(string codec, int monitor)
    {
        foreach (string c in Candidatos(codec))
            if (Testar(c, monitor)) return c;
        return null;
    }

    static bool Testar(string enc, int monitor)
    {
        lock (testados) { bool r; if (testados.TryGetValue(enc, out r)) return r; }
        Opcoes o = new Opcoes();
        o.Fps = 30; o.Mbps = 5; o.Monitor = monitor; o.Codificador = enc;
        string vf = Filtro(o);
        string args = "-hide_banner -loglevel error -nostdin -f lavfi -i ddagrab=output_idx=" + monitor +
                      ":framerate=30 -frames:v 5 " + (vf != null ? "-vf \"" + vf + "\" " : "") + ArgsCodificador(o) + "-f null -";
        string saida;
        int cod = Ffmpeg.Rodar(args, null, 20000, out saida);
        bool ok = cod == 0;
        if (!ok) App.Log("Codificador " + enc + " indisponível (" + cod + "): " + Ffmpeg.Resumo(saida));
        if (cod != -1 && cod != -2) lock (testados) testados[enc] = ok;   // tempo esgotado nao conta
        return ok;
    }

    // true = funciona, false = nao funciona, null = ainda nao testado
    public static bool? Disponivel(string codec)
    {
        bool falta = false;
        lock (testados)
            foreach (string c in Candidatos(codec))
            {
                bool r;
                if (!testados.TryGetValue(c, out r)) falta = true;
                else if (r) return true;
            }
        return falta ? (bool?)null : false;
    }

    // Descobre em fundo quais formatos a placa aceita (para a tela de configuracoes)
    public static void TestarCodecs()
    {
        if (testando || Ffmpeg.Caminho == null) return;
        testando = true;
        Screen tela = MonitorEscolhido();
        Thread t = new Thread(delegate()
        {
            int m = Math.Max(0, Dxgi.IndiceDoMonitor(tela.DeviceName));
            foreach (string c in ConfigClipe.Codecs) Escolher(c, m);
            NaJanela(delegate { testando = false; Avisar(); });
        });
        t.IsBackground = true;
        t.Start();
    }

    // ---- Linha de comando ----
    public static string Filtro(Opcoes o)
    {
        bool naPlaca = o.Codificador.EndsWith("_nvenc") || o.Codificador.EndsWith("_amf");
        if (naPlaca && o.Altura == 0) return null;   // tudo na placa, sem copiar a imagem
        string f = "hwdownload,format=bgra";
        if (o.Altura > 0)
            f += ",scale=w='if(gt(iw,ih),-2,min(" + o.Altura + ",iw))':h='if(gt(iw,ih),min(" + o.Altura + ",ih),-2)':flags=bilinear";
        return f + (o.Codificador == "libx264" ? ",format=yuv420p" : ",format=nv12");
    }

    public static string ArgsCodificador(Opcoes o)
    {
        int k = o.Mbps * 1000;
        string taxa = "-b:v " + k + "k -maxrate " + (k * 3 / 2) + "k -bufsize " + (k * 2) + "k ";
        string e = o.Codificador, g = "-g " + o.Fps + " ";
        if (e.EndsWith("_nvenc")) return "-c:v " + e + " -preset p4 -tune hq -rc vbr " + taxa + "-spatial-aq 1 -forced-idr 1 " + g;
        if (e.EndsWith("_amf"))   return "-c:v " + e + " -quality balanced -rc vbr_peak " + taxa + g;
        return "-c:v " + e + " -preset veryfast " + taxa + g;
    }

    // Captura e codificacao; o replay e a gravacao so mudam a saida.
    // comTeclado = o ffmpeg le o "q" para terminar o arquivo direito.
    public static string ArgsCaptura(Opcoes o, string entradaPc, string entradaMic, bool comTeclado)
    {
        StringBuilder a = new StringBuilder("-hide_banner -loglevel error " + (comTeclado ? "" : "-nostdin ") + "-y ");
        a.Append("-f lavfi -i ddagrab=output_idx=").Append(o.Monitor).Append(":framerate=").Append(o.Fps)
         .Append(":draw_mouse=").Append(o.MostrarCursor ? 1 : 0).Append(' ');
        if (entradaPc != null) a.Append(entradaPc).Append(' ');
        if (entradaMic != null) a.Append(entradaMic).Append(' ');
        string vf = Filtro(o);
        if (vf != null) a.Append("-vf \"").Append(vf).Append("\" ");
        a.Append("-map 0:v ");
        if (entradaPc != null && entradaMic != null)
            a.Append("-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest:normalize=0[som]\" -map \"[som]\" ");
        else if (entradaPc != null || entradaMic != null)
            a.Append("-map 1:a ");
        a.Append(ArgsCodificador(o));
        if (entradaPc != null || entradaMic != null) a.Append("-c:a aac -b:a 192k -ac 2 -ar 48000 ");
        return a.ToString();
    }

    // Nomes dos arquivos de uma sessao do anel: s00007_0012.ts e lista_s00007.csv
    public static string NomeSessao(int sessao) { return "s" + sessao.ToString("00000"); }
    public static string ListaDaSessao(int sessao) { return "lista_" + NomeSessao(sessao) + ".csv"; }

    public static string MontarArgs(Opcoes o, string entradaPc, string entradaMic, int sessao, double deslocamento)
    {
        StringBuilder a = new StringBuilder(ArgsCaptura(o, entradaPc, entradaMic, false));
        a.Append("-force_key_frames \"expr:gte(t,n_forced*1)\" ");
        // O relogio desta sessao continua de onde o anel estava: emendar com as anteriores nao volta no tempo
        if (deslocamento > 0)
            a.Append("-output_ts_offset ").Append(deslocamento.ToString("0.000", CultureInfo.InvariantCulture)).Append(' ');
        int n = o.Segundos + Folga;
        a.Append("-f segment -segment_time 1 -segment_wrap ").Append(n)
         .Append(" -segment_list ").Append(ListaDaSessao(sessao)).Append(" -segment_list_type csv -segment_list_size ").Append(n)
         .Append(" -segment_format mpegts -max_muxing_queue_size 4096 ").Append(NomeSessao(sessao)).Append("_%04d.ts");
        return a.ToString();
    }

    // Sessoes que tem lista no anel, da mais velha para a mais nova
    static List<int> Sessoes(string pasta)
    {
        List<int> r = new List<int>();
        try
        {
            foreach (string f in Directory.GetFiles(pasta, "lista_s*.csv"))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                int s;
                if (int.TryParse(n.Substring(7), out s)) r.Add(s);
            }
        }
        catch { }
        r.Sort();
        return r;
    }

    static string LerLista(string arquivo)
    {
        string texto = null;
        for (int t = 0; t < 5 && string.IsNullOrEmpty(texto); t++)
        {
            try
            {
                using (FileStream f = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader r = new StreamReader(f))
                    texto = r.ReadToEnd();
            }
            catch (FileNotFoundException) { return null; }
            catch { }
            if (string.IsNullOrEmpty(texto)) Thread.Sleep(40);
        }
        return texto;
    }

    // Os ultimos pedacos prontos, em ordem (passando por todas as sessoes que
    // ainda estao no anel), mais o que esta sendo gravado agora
    public static List<string> EscolherPedacos(string pasta, int segundos, int tamanhoAnel)
    {
        List<string> nomes = new List<string>();
        List<int> sessoes = Sessoes(pasta);
        string ultimoPronto = null;
        int ultimaSessao = sessoes.Count > 0 ? sessoes[sessoes.Count - 1] : -1;
        List<string> daUltima = new List<string>();
        // Mais velho que isso a limpeza pode apagar durante o salvamento (ela usa + 10 s)
        DateTime velho = DateTime.UtcNow.AddSeconds(-(segundos + Folga + 5));

        foreach (int s in sessoes)
        {
            string texto = LerLista(Path.Combine(pasta, ListaDaSessao(s)));
            if (string.IsNullOrEmpty(texto)) continue;
            string pre = NomeSessao(s) + "_";
            foreach (string l in texto.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int v = l.IndexOf(',');
                string n = (v > 0 ? l.Substring(0, v) : l).Trim();
                if (!n.StartsWith(pre) || !n.EndsWith(".ts")) continue;
                string c = Path.Combine(pasta, n);
                if (!File.Exists(c)) continue;
                if (s == ultimaSessao) daUltima.Add(n);
                try { if (File.GetLastWriteTimeUtc(c) < velho) continue; } catch { continue; }
                nomes.Add(n);
            }
        }
        if (nomes.Count > 0) ultimoPronto = nomes[nomes.Count - 1];
        if (nomes.Count > segundos) nomes.RemoveRange(0, nomes.Count - segundos);

        // O pedaco que a sessao mais nova esta gravando: o seguinte ao ultimo
        // pronto dela (ou o primeiro, se ela acabou de comecar), se for mais novo
        if (ultimaSessao >= 0)
        {
            int prox = 0;
            if (daUltima.Count > 0)
            {
                string u = daUltima[daUltima.Count - 1];
                int num;
                if (!int.TryParse(u.Substring(u.Length - 7, 4), out num)) return nomes;
                prox = (num + 1) % tamanhoAnel;
            }
            string nome = NomeSessao(ultimaSessao) + "_" + prox.ToString("0000") + ".ts";
            string cp = Path.Combine(pasta, nome);
            try
            {
                if (File.Exists(cp) && new FileInfo(cp).Length > 0 && !nomes.Contains(nome) &&
                    (ultimoPronto == null ||
                     File.GetLastWriteTimeUtc(cp) >= File.GetLastWriteTimeUtc(Path.Combine(pasta, ultimoPronto)).AddMilliseconds(-500)))
                    nomes.Add(nome);
            }
            catch { }
        }
        return nomes;
    }

    // A sessao acabou a forca (perdeu a tela, travou): o pedaco que ela estava
    // gravando tem o ultimo segundo antes da queda, que pode ser o fim da
    // jogada. Com o ffmpeg ja fechado, acerta o fim dele e o poe na lista.
    public static void FecharSessao(string pasta, int sessao, int tamanhoAnel)
    {
        if (sessao < 0 || tamanhoAnel <= 0) return;
        try
        {
            string lista = Path.Combine(pasta, ListaDaSessao(sessao));
            string texto = (File.Exists(lista) ? LerLista(lista) : null) ?? "";
            string ultimo = null;
            foreach (string l in texto.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int v = l.IndexOf(',');
                ultimo = (v > 0 ? l.Substring(0, v) : l).Trim();
            }
            int prox = 0, num;
            if (ultimo != null)
            {
                if (ultimo.Length < 11 || !int.TryParse(ultimo.Substring(ultimo.Length - 7, 4), out num)) return;
                prox = (num + 1) % tamanhoAnel;
            }
            string nome = NomeSessao(sessao) + "_" + prox.ToString("0000") + ".ts";
            string c = Path.Combine(pasta, nome);
            if (!File.Exists(c) || new FileInfo(c).Length < 188 * 20 || texto.Contains(nome + ",")) return;
            // Mais velho que o ultimo pronto = sobra da volta anterior do anel
            if (ultimo != null && File.Exists(Path.Combine(pasta, ultimo)) &&
                File.GetLastWriteTimeUtc(c) < File.GetLastWriteTimeUtc(Path.Combine(pasta, ultimo))) return;
            Gravacao.CortarQuadroIncompleto(c);
            File.AppendAllText(lista, (texto.Length > 0 && !texto.EndsWith("\n") ? "\n" : "") + nome + ",0,0\n");
        }
        catch (Exception ex) { App.Log("Erro ao fechar a sessao do replay: " + ex.Message); }
    }

    // O pedaco ja esta pronto (o ffmpeg o pos na lista da sessao dele)?
    public static bool Listado(string pasta, string pedaco)
    {
        int s;
        if (pedaco.Length < 7 || !int.TryParse(pedaco.Substring(1, 5), out s)) return false;
        string texto = LerLista(Path.Combine(pasta, ListaDaSessao(s)));
        if (string.IsNullOrEmpty(texto)) return false;
        foreach (string l in texto.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            if (l.StartsWith(pedaco + ",") || l.Trim() == pedaco) return true;
        return false;
    }

    // Tira do anel as sessoes velhas: pedacos mais antigos que a duracao do
    // clipe (com folga) nunca entram num clipe. A sessao que esta gravando
    // agora so perde o que o proprio ffmpeg ja deixou para tras.
    public static void LimparAnel(string pasta, int segundos, bool tambemAtual)
    {
        try
        {
            if (!Directory.Exists(pasta)) return;
            DateTime limite = DateTime.UtcNow.AddSeconds(-(segundos + Folga + 10));
            string atual = sessaoAtual >= 0 && !tambemAtual ? NomeSessao(sessaoAtual) + "_" : null;
            foreach (string f in Directory.GetFiles(pasta, "s*_*.ts"))
            {
                string n = Path.GetFileName(f);
                if (atual != null && n.StartsWith(atual)) continue;
                try { if (File.GetLastWriteTimeUtc(f) < limite) File.Delete(f); } catch { }
            }
            // Lista sem nenhum pedaco = sessao acabou
            foreach (int s in Sessoes(pasta))
            {
                if (s == sessaoAtual) continue;
                if (Directory.GetFiles(pasta, NomeSessao(s) + "_*.ts").Length == 0)
                    try { File.Delete(Path.Combine(pasta, ListaDaSessao(s))); } catch { }
            }
            // Sobras da versao antiga do anel
            foreach (string f in Directory.GetFiles(pasta, "seg*.ts")) try { File.Delete(f); } catch { }
            string velha = Path.Combine(pasta, "lista.csv");
            if (File.Exists(velha)) try { File.Delete(velha); } catch { }
        }
        catch { }
    }

    public static string CaminhoNovo(string prefixo = "clipe-", string ext = ".mp4")
    {
        Directory.CreateDirectory(ConfigClipe.Pasta);
        string b = prefixo + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string arq = Path.Combine(ConfigClipe.Pasta, b + ext);
        for (int n = 2; File.Exists(arq); n++) arq = Path.Combine(ConfigClipe.Pasta, b + "_" + n + ext);
        return arq;
    }

    public static string Tamanho(long bytes)
    {
        if (bytes < 1024 * 1024) return Math.Max(1, bytes / 1024) + " KB";
        double mb = bytes / (1024.0 * 1024.0);
        return mb < 1024 ? mb.ToString(mb < 10 ? "0.0" : "0") + " MB" : (mb / 1024).ToString("0.0") + " GB";
    }

    // ---- Salvar ----
    public static void SalvarClipe()
    {
        if (!ConfigClipe.Ligado)
        {
            Aviso.Mostrar("Replay instantâneo desligado",
                "Ligue com " + Config.Atalhos[Config.AlternarReplay] + " ou na galeria (" + Config.Atalhos[Config.AbrirGaleria] + ")", Tema.Perigo);
            return;
        }
        if (Salvando) return;

        // Travada sem o ffmpeg fechar: reinicia, e o que foi gravado antes continua no anel
        if (Gravando && vigia != null && (vigia.TelaPerdida || vigia.Parado > 2000)) Travou();

        // Reiniciando (acabou de perder a tela): salva o que o anel tem
        List<string> pedacos = SegundosAtuais > 0 ? EscolherPedacos(ConfigClipe.PastaAnel, SegundosAtuais, anel) : new List<string>();
        if (!Gravando && pedacos.Count == 0)
        {
            Aviso.Mostrar("O replay não está gravando", Iniciando ? "Ainda está iniciando, tente de novo" : StatusLongo, Tema.Perigo);
            return;
        }
        if (pedacos.Count == 0)
        {
            Aviso.Mostrar("Ainda não há o que salvar", "O replay acabou de começar", Tema.Suave);
            return;
        }
        Salvando = true;
        Avisar();

        string destino = CaminhoNovo();
        bool hevc = Codificador != null && Codificador.StartsWith("hevc");
        string ultimo = pedacos[pedacos.Count - 1];
        bool emAndamento = !Listado(ConfigClipe.PastaAnel, ultimo);
        bool daSessaoAtual = Gravando && ultimo.StartsWith(NomeSessao(sessaoAtual) + "_");

        // Nao e de fundo de proposito: sair logo depois nao perde o clipe
        Thread t = new Thread(delegate()
        {
            // O pedaco que estava sendo gravado termina no meio de um quadro (o
            // fim do clipe saia com blocos cinza): espera ele fechar (ate ~1 s) ou,
            // se a sessao dele ja acabou (perdeu a tela), fica de fora
            if (emAndamento)
            {
                bool fechou = false;
                for (int i = 0; daSessaoAtual && i < 30 && !fechou; i++)
                {
                    Thread.Sleep(50);
                    fechou = Listado(ConfigClipe.PastaAnel, ultimo);
                }
                if (!fechou && pedacos.Count > 1) pedacos.RemoveAt(pedacos.Count - 1);
            }
            int segundos = pedacos.Count;
            string args = "-hide_banner -loglevel error -nostdin -y -i \"concat:" + string.Join("|", pedacos.ToArray()) +
                          "\" -map 0 -c copy " + (hevc ? "-tag:v hvc1 " : "") + "-movflags +faststart " + Ffmpeg.Aspas(destino);
            string saida = "", duracao = null;
            int cod = -3;
            try { cod = Ffmpeg.Rodar(args, ConfigClipe.PastaAnel, 60000, out saida); }
            catch (Exception ex) { saida = ex.Message; }
            long tam = 0;
            try { if (File.Exists(destino)) tam = new FileInfo(destino).Length; } catch { }
            bool ok = cod == 0 && tam > 0;
            if (ok)
            {
                try { Bitmap b = Miniaturas.Ler(destino, true, out duracao); if (b != null) b.Dispose(); } catch { }
            }
            else
            {
                App.Log("Erro ao salvar clipe (" + cod + "): " + saida);
                try { if (File.Exists(destino)) File.Delete(destino); } catch { }
            }
            NaJanela(delegate
            {
                Salvando = false;
                if (ok)
                {
                    if (Config.SomAoCapturar) Som.Tocar();
                    Aviso.Mostrar("Clipe salvo", (duracao ?? ConfigClipe.NomeDuracao(segundos)) + Tema.Ponto + Tamanho(tam) +
                                  Tema.Ponto + Path.GetFileName(destino), Tema.Sucesso);
                    if (Galeria.Atual != null) Galeria.Atual.Recarregar();
                }
                else Aviso.Mostrar("Não deu para salvar o clipe", Ffmpeg.Resumo(saida) ?? "erro " + cod, Tema.Perigo);
                Avisar();
            });
        });
        t.Start();
    }

    // ---- Baixar o ffmpeg pela tela de configuracoes ----
    public static void BaixarFfmpeg()
    {
        if (ProgressoDownload >= 0) return;
        ProgressoDownload = 0;
        Avisar();
        Ffmpeg.Baixar(
            delegate(int pct) { NaJanela(delegate { ProgressoDownload = pct; Avisar(); }); },
            delegate(string erro)
            {
                NaJanela(delegate
                {
                    ProgressoDownload = -1;
                    if (erro == null)
                    {
                        Aviso.Mostrar("ffmpeg instalado", "O replay instantâneo já pode gravar", Tema.Sucesso);
                        falhas = 0;
                        if (ConfigClipe.Ligado) Iniciar();
                    }
                    else Aviso.Mostrar("Não deu para baixar o ffmpeg", erro, Tema.Perigo);
                    Avisar();
                });
            });
    }
}

// Miniaturas da galeria, guardadas em disco: abrir a galeria de novo e instantaneo
static class Miniaturas
{
    public const int Largura = 384, Altura = 216;
    static readonly Color FundoMini = Color.FromArgb(16, 16, 20);

    static string Chave(FileInfo f)
    {
        ulong h = 14695981039346656037UL;   // FNV-1a
        foreach (char c in f.FullName.ToLowerInvariant() + "|" + f.Length + "|" + f.LastWriteTimeUtc.Ticks)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h.ToString("x16");
    }

    // Roda fora da thread da janela. null = nao conseguiu.
    public static Bitmap Ler(string caminho, bool video, out string duracao)
    {
        duracao = null;
        FileInfo f = new FileInfo(caminho);
        if (!f.Exists) return null;
        string ch = Chave(f);
        string jpg = Path.Combine(ConfigClipe.PastaMiniaturas, ch + ".jpg");
        string txt = Path.Combine(ConfigClipe.PastaMiniaturas, ch + ".txt");
        if (video && File.Exists(txt)) try { duracao = File.ReadAllText(txt); } catch { }
        if (File.Exists(jpg)) try { return Abrir(jpg); } catch { }
        Directory.CreateDirectory(ConfigClipe.PastaMiniaturas);

        if (video)
        {
            string saida;
            Ffmpeg.Rodar("-hide_banner -nostdin -y -ss 0.5 -i " + Ffmpeg.Aspas(caminho) + " -frames:v 1 -vf \"scale=" + Largura + ":" + Altura +
                         ":force_original_aspect_ratio=decrease,pad=" + Largura + ":" + Altura + ":(ow-iw)/2:(oh-ih)/2:color=0x101014\" -q:v 3 " +
                         Ffmpeg.Aspas(jpg), null, 20000, out saida);
            string d = LerDuracao(saida);
            if (d != null) { duracao = d; try { File.WriteAllText(txt, d); } catch { } }
            if (File.Exists(jpg)) try { return Abrir(jpg); } catch { }
            return null;
        }

        using (Bitmap p = Historico.LerPrevia(caminho, Largura, Altura))
        {
            Bitmap b = new Bitmap(Largura, Altura, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(FundoMini);
                g.DrawImage(p, (Largura - p.Width) / 2, (Altura - p.Height) / 2, p.Width, p.Height);
            }
            try { SalvarJpg(b, jpg); } catch { }
            return b;
        }
    }

    static Bitmap Abrir(string jpg)
    {
        using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(jpg)))
        using (Image img = Image.FromStream(ms))
        {
            Bitmap b = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b)) g.DrawImage(img, 0, 0, img.Width, img.Height);
            return b;
        }
    }

    static void SalvarJpg(Bitmap b, string arq)
    {
        foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
            if (c.FormatID == ImageFormat.Jpeg.Guid)
                using (EncoderParameters p = new EncoderParameters(1))
                {
                    p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                    b.Save(arq, c, p);
                    return;
                }
    }

    // "Duration: 00:00:30.03" -> "0:30"
    public static string LerDuracao(string saida)
    {
        if (saida == null) return null;
        int i = saida.IndexOf("Duration: ");
        if (i < 0 || saida.Length < i + 21) return null;
        TimeSpan t;
        if (!TimeSpan.TryParse(saida.Substring(i + 10, 11), CultureInfo.InvariantCulture, out t)) return null;
        int s = (int)Math.Round(t.TotalSeconds);
        return s >= 3600 ? (s / 3600) + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00")
                         : (s / 60) + ":" + (s % 60).ToString("00");
    }
}

// Gravacao normal (Alt + F9): grava ate parar. Vai em partes .ts (tocaveis
// mesmo se o PC desligar no meio) numa pasta da sessao e, ao parar, as partes
// viram um .mp4 so, sem recodificar. Se a captura cair (Alt+Tab num jogo em
// tela cheia), abre outra parte com o relogio continuando: o video final fica
// inteiro, com um salto de ~1 s no lugar da queda.
static class Gravacao
{
    public static bool Ativa, Iniciando, Parando;
    public static DateTime Inicio;
    public static readonly string PastaTemp = Path.Combine(ConfigClipe.Local, "gravando");

    static Process proc;
    static Vigia vigia;
    static SomCaptura pc, mic;
    static string sessao, codificador;   // sessao = pasta com as partes
    static int geracao, parte;
    static readonly Stopwatch relogio = new Stopwatch();
    static DateTime ultimoAvisoTravou;
    static Indicador indicador;
    static readonly List<string> linhas = new List<string>();

    public static string Tempo
    {
        get
        {
            TimeSpan t = DateTime.Now - Inicio;
            return t.TotalHours >= 1 ? (int)t.TotalHours + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                                     : (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
        }
    }

    public static void Alternar()
    {
        if (Ativa) Parar();
        else if (!Iniciando && !Parando) Iniciar(false);
    }

    public static void Iniciar(bool continuacao)
    {
        if (Ativa || Iniciando || Parando) return;
        if (Ffmpeg.Caminho == null)
        {
            Aviso.Mostrar("Falta o ffmpeg", "Baixe nas configurações da galeria (" + Config.Atalhos[Config.AbrirGaleria] + ")", Tema.Perigo);
            return;
        }
        Iniciando = true;
        Replay.Notificar();
        Replay.Opcoes o = Replay.OpcoesAtuais();
        Screen tela = Replay.MonitorEscolhido();
        bool comPc = ConfigClipe.SomPc, comMic = ConfigClipe.Microfone;
        int g = ++geracao;
        if (!continuacao || sessao == null)
        {
            sessao = Path.Combine(PastaTemp, "gravacao-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            parte = 0;
            relogio.Restart();
        }
        string pasta = sessao;
        int numero = ++parte;
        double deslocamento = numero == 1 ? 0 : relogio.Elapsed.TotalSeconds;

        Thread t = new Thread(delegate()
        {
            SomCaptura a = null, m = null;
            try
            {
                o.Monitor = Math.Max(0, Dxgi.IndiceDoMonitor(tela.DeviceName));
                o.Codificador = Replay.Escolher(o.Codec, o.Monitor);
                if (o.Codificador == null) throw new Exception("a placa de vídeo não aceitou " + Replay.NomeCodec(o.Codec));
                Directory.CreateDirectory(pasta);
                string arq = Path.Combine(pasta, NomeParte(numero));
                int pid = Process.GetCurrentProcess().Id;
                if (comPc) a = Replay.AbrirSom(false, "TrajadoTools-gpc-" + pid + "-" + g);
                if (comMic) m = Replay.AbrirSom(true, "TrajadoTools-gmic-" + pid + "-" + g);

                string args = Ffmpeg.ArgsProgresso + Replay.ArgsCaptura(o, a == null ? null : a.Entrada, m == null ? null : m.Entrada, true) +
                              (deslocamento > 0 ? "-output_ts_offset " + deslocamento.ToString("0.000", CultureInfo.InvariantCulture) + " " : "") +
                              "-f mpegts " + Ffmpeg.Aspas(arq);
                lock (linhas) linhas.Clear();
                Process p = new Process();
                p.StartInfo = Ffmpeg.Info(args, pasta);
                p.StartInfo.RedirectStandardInput = true;
                Vigia v = new Vigia();
                Ffmpeg.Vigiar(p, v);
                p.EnableRaisingEvents = true;
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    lock (linhas) { linhas.Add(e.Data); if (linhas.Count > 12) linhas.RemoveAt(0); }
                    if (!v.TelaPerdida && Vigia.PerdeuATela(e.Data))
                    {
                        v.TelaPerdida = true;
                        Replay.NaJanela(delegate { if (vigia == v) Caiu(g, p, "a captura perdeu a tela"); });
                    }
                };
                p.Exited += delegate { Replay.NaJanela(delegate { Caiu(g, p, null); }); };
                p.Start();
                Trabalho.Prender(p);
                try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();

                SomCaptura af = a, mf = m;
                a = null;
                m = null;
                Replay.NaJanela(delegate
                {
                    proc = p;
                    vigia = v;
                    pc = af;
                    mic = mf;
                    codificador = o.Codificador;
                    Iniciando = false;
                    Ativa = true;
                    if (!continuacao) Inicio = DateTime.Now;
                    if (p.HasExited) { Caiu(g, p, null); return; }
                    if (v.TelaPerdida) { Caiu(g, p, "a captura perdeu a tela"); return; }
                    if (indicador == null) { indicador = new Indicador(tela.Bounds); indicador.Show(); }
                    if (!continuacao)
                        Aviso.Mostrar("Gravação iniciada", Config.Atalhos[Config.Gravar] + " para parar", Tema.Perigo);
                    Replay.Notificar();
                });
            }
            catch (Exception ex)
            {
                if (a != null) a.Dispose();
                if (m != null) m.Dispose();
                App.Log("Erro ao iniciar a gravacao: " + ex);
                Replay.NaJanela(delegate
                {
                    Iniciando = false;
                    FecharIndicador();
                    Aviso.Mostrar("Não deu para gravar", ex.Message, Tema.Perigo);
                    // Se ja havia partes gravadas (continuacao), elas nao se perdem
                    if (continuacao) Encerrar(ex.Message, false, true);
                    Replay.Notificar();
                });
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    public static string NomeParte(int n) { return "parte" + n.ToString("0000") + ".ts"; }

    // Chamado a cada segundo pelo replay
    public static void Vigiar()
    {
        if (indicador != null) indicador.Invalidate();
        if (Ativa) Replay.Notificar();   // tempo na galeria
        if (Ativa && vigia != null && (vigia.TelaPerdida || vigia.Parado > Replay.TravadoMs))
            Caiu(geracao, proc, vigia.TelaPerdida ? "a captura perdeu a tela" : "a captura da tela travou");
    }

    public static void Parar() { Encerrar(null, false); }

    // O ffmpeg fechou ou travou no meio. Travou = continua numa parte nova, no mesmo video.
    static void Caiu(int g, Process p, string travou)
    {
        if (g != geracao || proc != p || !Ativa) return;
        string saida;
        lock (linhas) saida = string.Join("\n", linhas.ToArray());
        App.Log("Gravacao interrompida (" + (travou ?? "ffmpeg fechou") + ", parte " + parte + "): " + saida);
        Encerrar(travou ?? Ffmpeg.Resumo(saida) ?? "o ffmpeg fechou", travou != null);
    }

    static void Encerrar(string motivo, bool continuar, bool jaParado = false)
    {
        if (!Ativa && !jaParado) return;
        geracao++;
        Ativa = false;
        Parando = true;
        Process p = proc;
        SomCaptura a = pc, m = mic;
        string pasta = sessao, enc = codificador;
        bool perdida = motivo == "a captura perdeu a tela";
        proc = null; vigia = null; pc = null; mic = null;
        if (!continuar) { FecharIndicador(); sessao = null; }
        Replay.Notificar();

        // Nao e de fundo: sair do programa no meio nao perde a gravacao
        Thread t = new Thread(delegate()
        {
            Terminar(p, a, m, continuar && !perdida);   // congelado nao le o "q"
            string destino = null, info = null;
            bool ok = continuar || Finalizar(pasta, enc, out destino, out info);
            Replay.NaJanela(delegate
            {
                Parando = false;
                if (continuar)
                {
                    // Perder a tela e normal (Alt+Tab num jogo em tela cheia): segue calado
                    if (!perdida && (DateTime.Now - ultimoAvisoTravou).TotalSeconds > 60)
                    {
                        ultimoAvisoTravou = DateTime.Now;
                        Aviso.Mostrar("A captura travou", "A gravação continua no mesmo vídeo", Tema.Suave);
                    }
                    Iniciar(true);
                }
                else if (ok)
                {
                    if (Config.SomAoCapturar) Som.Tocar();
                    Aviso.Mostrar(motivo == null ? "Gravação salva" : "Gravação interrompida: " + motivo, info, motivo == null ? Tema.Sucesso : Tema.Perigo);
                }
                else Aviso.Mostrar("Não deu para salvar a gravação", info, Tema.Perigo);
                if (Galeria.Atual != null) Galeria.Atual.Recarregar();
                Replay.Notificar();
            });
        });
        t.Start();
    }

    // "q" faz o ffmpeg fechar o arquivo direito
    static void Terminar(Process p, SomCaptura a, SomCaptura m, bool matar = false)
    {
        if (p != null)
        {
            if (matar) { try { if (!p.HasExited) p.Kill(); p.WaitForExit(3000); } catch { } }
            else
            {
                try { if (!p.HasExited) { p.StandardInput.Write("q"); p.StandardInput.Flush(); } } catch { }
                try { if (!p.WaitForExit(15000)) p.Kill(); } catch { }
            }
        }
        if (a != null) a.Dispose();
        if (m != null) m.Dispose();
    }

    // Partes da sessao, em ordem, sem as vazias
    public static List<string> Partes(string pasta)
    {
        List<string> r = new List<string>();
        if (pasta == null || !Directory.Exists(pasta)) return r;
        foreach (string f in Directory.GetFiles(pasta, "parte*.ts"))
        {
            try { if (new FileInfo(f).Length > 0) r.Add(Path.GetFileName(f)); } catch { }
        }
        r.Sort(StringComparer.OrdinalIgnoreCase);
        return r;
    }

    // Um ffmpeg fechado a forca deixa o ultimo quadro pela metade (blocos
    // cinza na emenda). Corta o .ts no comeco do ultimo quadro de video: o
    // arquivo e feito de pacotes de 188 bytes e o quadro comeca num pacote com
    // inicio de PES de video (00 00 01 E0..EF). Perder 1 quadro nao se ve.
    public static void CortarQuadroIncompleto(string arq)
    {
        try
        {
            using (FileStream f = new FileStream(arq, FileMode.Open, FileAccess.ReadWrite))
            {
                long n = f.Length / 188;
                byte[] b = new byte[188];
                long limite = Math.Max(1, n - 100000);   // ~19 MB para tras: um quadro nunca e maior
                for (long i = n - 1; i >= limite; i--)
                {
                    f.Position = i * 188;
                    if (f.Read(b, 0, 188) != 188 || b[0] != 0x47 || (b[1] & 0x40) == 0) continue;
                    int afc = (b[3] >> 4) & 3, p = 4;
                    if (afc == 2 || afc == 0) continue;   // sem dados
                    if (afc == 3) p += 1 + b[4];
                    if (p + 4 > 188) continue;
                    if (b[p] == 0 && b[p + 1] == 0 && b[p + 2] == 1 && b[p + 3] >= 0xE0 && b[p + 3] <= 0xEF)
                    {
                        f.SetLength(i * 188);
                        return;
                    }
                }
            }
        }
        catch (Exception ex) { App.Log("Erro ao acertar o fim da parte " + Path.GetFileName(arq) + ": " + ex.Message); }
    }

    // Partes -> um .mp4 na pasta dos clipes (o .ts aceita ser emendado byte a
    // byte, como no replay). Se nao der, vai um .ts emendado, que tambem toca.
    // todasCortadas = recuperando depois de o app cair (nenhuma parte fechou direito).
    static bool Finalizar(string pasta, string enc, out string destino, out string info, bool todasCortadas = false)
    {
        destino = null;
        info = "";
        try
        {
            List<string> partes = Partes(pasta);
            if (partes.Count == 0)
            {
                info = "nada foi gravado";
                try { if (pasta != null && Directory.Exists(pasta)) Directory.Delete(pasta, true); } catch { }
                return false;
            }
            // So a ultima parte fechou com o "q"; as outras cairam no meio
            for (int i = 0; i < partes.Count; i++)
                if (todasCortadas || i < partes.Count - 1) CortarQuadroIncompleto(Path.Combine(pasta, partes[i]));
            partes = Partes(pasta);
            string nome = Path.GetFileName(pasta);
            destino = Path.Combine(ConfigClipe.Pasta, nome + ".mp4");
            Directory.CreateDirectory(ConfigClipe.Pasta);
            for (int n = 2; File.Exists(destino); n++) destino = Path.Combine(ConfigClipe.Pasta, nome + "_" + n + ".mp4");
            bool hevc = enc != null && enc.StartsWith("hevc");
            string saida;
            int cod = Ffmpeg.Rodar("-hide_banner -loglevel error -nostdin -y -i \"concat:" + string.Join("|", partes.ToArray()) +
                                   "\" -map 0 -c copy " + (hevc ? "-tag:v hvc1 " : "") + "-movflags +faststart " + Ffmpeg.Aspas(destino),
                                   pasta, 600000, out saida);
            if (!(cod == 0 && File.Exists(destino) && new FileInfo(destino).Length > 0))
            {
                App.Log("Erro ao converter a gravacao (" + cod + "): " + saida);
                try { if (File.Exists(destino)) File.Delete(destino); } catch { }
                destino = Path.ChangeExtension(destino, ".ts");
                using (FileStream fo = File.Create(destino))
                    foreach (string pt in partes)
                        using (FileStream fi = File.OpenRead(Path.Combine(pasta, pt))) fi.CopyTo(fo);
            }
            Directory.Delete(pasta, true);
            string dur;
            try { Bitmap b = Miniaturas.Ler(destino, true, out dur); if (b != null) b.Dispose(); } catch { dur = null; }
            info = (dur != null ? dur + Tema.Ponto : "") + Replay.Tamanho(new FileInfo(destino).Length) + Tema.Ponto + Path.GetFileName(destino);
            return true;
        }
        catch (Exception ex)
        {
            App.Log("Erro ao finalizar a gravacao: " + ex);
            info = ex.Message;
            return false;
        }
    }

    // O app fechou ou caiu no meio de uma gravacao: as partes viram video na
    // pasta dos clipes (em fundo: uma gravacao longa leva alguns segundos)
    public static void Recuperar()
    {
        try
        {
            if (!Directory.Exists(PastaTemp)) return;
            // Gravacoes da versao 1.7 (um .mkv so)
            foreach (string f in Directory.GetFiles(PastaTemp, "*.mkv"))
            {
                if (new FileInfo(f).Length == 0) { File.Delete(f); continue; }
                Directory.CreateDirectory(ConfigClipe.Pasta);
                string d = Path.Combine(ConfigClipe.Pasta, Path.GetFileName(f));
                for (int n = 2; File.Exists(d); n++) d = Path.Combine(ConfigClipe.Pasta, Path.GetFileNameWithoutExtension(f) + "_" + n + ".mkv");
                File.Move(f, d);
            }
            string[] pastas = Directory.GetDirectories(PastaTemp, "gravacao-*");
            if (pastas.Length == 0) return;
            Thread t = new Thread(delegate()
            {
                foreach (string p in pastas)
                {
                    if (p == sessao) continue;   // gravando agora
                    string destino, info;
                    if (Finalizar(p, null, out destino, out info, true)) App.Log("Gravacao recuperada: " + info);
                }
                Replay.NaJanela(delegate { if (Galeria.Atual != null) Galeria.Atual.Recarregar(); });
            });
            t.Start();
        }
        catch (Exception ex) { App.Log("Erro ao recuperar gravacao: " + ex.Message); }
    }

    // Saindo do programa: fecha a parte atual e deixa para o Recuperar da proxima vez
    public static void ParaSair()
    {
        FecharIndicador();
        if (!Ativa) return;
        Ativa = false;
        geracao++;
        Terminar(proc, pc, mic);
        proc = null; pc = null; mic = null;
        sessao = null;
    }

    static void FecharIndicador()
    {
        if (indicador != null) { indicador.Close(); indicador = null; }
    }
}

// Janelas que nao devem aparecer nos clipes, gravacoes e prints (Windows 10 2004+)
static class Capturas
{
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, uint afinidade);
    public static void Esconder(IntPtr h)
    {
        try { SetWindowDisplayAffinity(h, 0x11); } catch { }   // WDA_EXCLUDEFROMCAPTURE
    }
}

// "● REC 1:23" no canto do monitor gravado; nao sai na gravacao
class Indicador : Form
{
    public Indicador(Rectangle tela)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(18, 18, 22);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Size = new Size(112, 32);
        Location = new Point(tela.Right - Width - 24, tela.Top + 24);
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x8;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Capturas.Esconder(Handle); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillEllipse(Tema.Pincel(Color.FromArgb(235, 60, 60)), 12, 10, 12, 12);
        TextRenderer.DrawText(g, "REC  " + Gravacao.Tempo, Tema.Negrito, new Rectangle(30, 0, Width - 34, Height), Tema.Texto, Tema.Linha);
    }
}

// Aviso no canto da tela, como o da NVIDIA. Nao rouba o foco do jogo e o
// mouse passa por ele.
class Aviso : Form
{
    static Aviso atual;
    readonly string titulo, texto;
    readonly Color cor;
    readonly Timer fim;

    Aviso(string titulo, string texto, Color cor)
    {
        this.titulo = titulo;
        this.texto = texto;
        this.cor = cor;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Tema.Painel;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        int w = Math.Max(TextRenderer.MeasureText(titulo, Tema.Negrito).Width, TextRenderer.MeasureText(texto, Tema.Pequena).Width) + 44;
        Size = new Size(Math.Max(300, Math.Min(560, w)), 70);
        IntPtr frente = Nativo.GetForegroundWindow();
        Rectangle tela = frente != IntPtr.Zero ? Screen.FromHandle(frente).Bounds : Screen.PrimaryScreen.Bounds;
        Location = new Point(tela.Right - Width - 24, tela.Top + (Gravacao.Ativa || Gravacao.Iniciando ? 68 : 24));

        fim = new Timer();
        fim.Interval = 3500;
        fim.Tick += delegate { fim.Stop(); Close(); };
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x8;   // sem foco, sem barra de tarefas, clique passa, no topo
            return cp;
        }
    }

    // Nao sai nos clipes e gravacoes
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Capturas.Esconder(Handle); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Tema.Painel);
        using (Pen p = new Pen(Tema.Borda)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        g.FillRectangle(Tema.Pincel(cor), 0, 0, 4, Height);
        TextRenderer.DrawText(g, titulo, Tema.Negrito, new Rectangle(20, 10, Width - 32, 26), Tema.Texto, Tema.Linha);
        TextRenderer.DrawText(g, texto, Tema.Pequena, new Rectangle(20, 36, Width - 32, 22), Tema.Suave, Tema.Linha);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        fim.Dispose();
        if (atual == this) atual = null;
        base.OnFormClosed(e);
    }

    public static void Mostrar(string titulo, string texto, Color cor)
    {
        try
        {
            if (atual != null) atual.Close();
            Aviso a = new Aviso(titulo, texto ?? "", cor);
            atual = a;
            a.Show();
            a.fim.Start();
        }
        catch (Exception ex) { App.Log("Erro ao mostrar aviso: " + ex.Message); }
    }
}

// Botao grande da barra da galeria: icone, titulo e uma linha de estado
class BotaoGrande : Control
{
    public static readonly Font FonteIcone = new Font("Segoe MDL2 Assets", 18f);
    readonly string icone, titulo;
    string sub = "";
    bool ativo, aceso, sobre;

    public BotaoGrande(string icone, string titulo)
    {
        this.icone = icone;
        this.titulo = titulo;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Size = new Size(290, 76);
        Cursor = Cursors.Hand;
    }

    public void Definir(string sub, bool ativo, bool aceso)
    {
        if (this.sub == sub && this.ativo == ativo && this.aceso == aceso) return;
        this.sub = sub;
        this.ativo = ativo;
        this.aceso = aceso;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { sobre = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { sobre = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Arredondado(new Rectangle(0, 0, Width - 1, Height - 1), 12))
        {
            g.FillPath(Tema.Pincel(ativo ? Tema.Selecao : sobre ? Tema.CampoSob : Tema.Campo), p);
            if (ativo) using (Pen pen = new Pen(Tema.Destaque, 2)) g.DrawPath(pen, p);
        }
        TextRenderer.DrawText(g, icone, FonteIcone, new Rectangle(14, 0, 44, Height), ativo ? Tema.Destaque : Tema.Texto, Tema.Centro);
        if (aceso) g.FillEllipse(Tema.Pincel(Tema.Perigo), 46, 18, 10, 10);
        TextRenderer.DrawText(g, titulo, Tema.Negrito, new Rectangle(66, 14, Width - 76, 24), Tema.Texto, Tema.Linha);
        TextRenderer.DrawText(g, sub, Tema.Pequena, new Rectangle(66, 40, Width - 76, 22), aceso ? Tema.Texto : Tema.Suave, Tema.Linha);
    }
}

// Barra deslizante (taxa de bits)
class Deslizador : Control
{
    int valor;
    bool arrastando;
    public int Minimo, Maximo = 100;
    public event EventHandler Mexeu, Soltou;

    public int Valor
    {
        get { return valor; }
        set { int v = Math.Max(Minimo, Math.Min(Maximo, value)); if (v != valor) { valor = v; Invalidate(); } }
    }

    public Deslizador()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Size = new Size(420, 30);
        Cursor = Cursors.Hand;
    }

    int X(int v) { return 10 + (Width - 20) * (v - Minimo) / Math.Max(1, Maximo - Minimo); }

    void Mover(int x)
    {
        int antes = valor;
        Valor = Minimo + (int)Math.Round((x - 10) * (double)(Maximo - Minimo) / Math.Max(1, Width - 20));
        if (valor != antes && Mexeu != null) Mexeu(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { arrastando = true; Mover(e.X); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (arrastando) Mover(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (arrastando) { arrastando = false; if (Soltou != null) Soltou(this, EventArgs.Empty); }
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int y = Height / 2, x = X(valor);
        using (GraphicsPath p = Tema.Arredondado(new Rectangle(10, y - 3, Width - 20, 6), 3)) g.FillPath(Tema.Pincel(Tema.Campo), p);
        if (x > 12) using (GraphicsPath p = Tema.Arredondado(new Rectangle(10, y - 3, x - 10, 6), 3)) g.FillPath(Tema.Pincel(Tema.Destaque), p);
        g.FillEllipse(Brushes.White, x - 9, y - 9, 18, 18);
    }
}

// Um print ou clipe da galeria
class Midia
{
    public string Caminho;
    public bool Video;
    public DateTime Data;
    public long Tamanho;
    public string Duracao;
    public Bitmap Mini;
    public bool Pedida;
    public bool Marcada;      // selecionada
    public Rectangle Caixa;   // posicao na grade, sem a rolagem

    public bool Gravacao { get { return Video && Path.GetFileName(Caminho).StartsWith("gravacao-", StringComparison.OrdinalIgnoreCase); } }
    public string Tipo { get { return !Video ? "Print" : Gravacao ? "Gravação" : "Clipe"; } }
}

// Grade de miniaturas agrupada por dia. So desenha o que aparece e carrega
// as miniaturas numa thread propria, as visiveis primeiro.
class GradeMidias : Control
{
    public const int Margem = 48, Espaco = 20, AlturaTexto = 54, AlturaDia = 50, LarguraMin = 280, MaxMinis = 150;
    static readonly CultureInfo Br = new CultureInfo("pt-BR");
    public static readonly Font FonteVazia = new Font("Segoe MDL2 Assets", 26f);

    List<Midia> itens = new List<Midia>();
    readonly List<KeyValuePair<int, string>> dias = new List<KeyValuePair<int, string>>();
    volatile int rolagem, alturaVista;
    int total, larg, alt;
    Midia sobre;
    bool arrastando;
    int arrasteY, arrasteRol;
    public event Action<Midia, MouseButtons, Point> Clicou;
    public event Action SelecaoMudou;
    public static readonly Font FonteMarca = new Font("Segoe MDL2 Assets", 10f);

    // Selecao: laco = retangulo arrastado com o mouse
    bool laco, pendente;
    Point lacoIni, lacoFim;   // no conteudo (com a rolagem)
    Point desceu;
    Midia ancora;             // ponto de partida do Shift + clique
    HashSet<Midia> antesDoLaco = new HashSet<Midia>();
    readonly Timer rolarLaco = new Timer();
    int marcadas;

    readonly Stack<Midia> pedidos = new Stack<Midia>();
    readonly AutoResetEvent temPedido = new AutoResetEvent(false);
    volatile bool parar;
    readonly Thread trabalhador;

    public GradeMidias()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.Selectable, true);
        rolarLaco.Interval = 30;
        rolarLaco.Tick += delegate { RolarNoLaco(); };
        trabalhador = new Thread(Carregador, 512 * 1024);
        trabalhador.IsBackground = true;
        trabalhador.Priority = ThreadPriority.BelowNormal;
        trabalhador.Start();
    }

    public void Definir(List<Midia> lista)
    {
        foreach (Midia m in itens) m.Marcada = false;
        foreach (Midia m in lista) m.Marcada = false;
        itens = lista;
        rolagem = 0;
        sobre = null;
        ancora = null;
        Organizar();
        SelecaoAlterada();
    }

    // ---- Selecao ----
    public int Marcadas { get { return marcadas; } }

    public List<Midia> Selecionadas()
    {
        List<Midia> l = new List<Midia>();
        foreach (Midia m in itens) if (m.Marcada) l.Add(m);
        return l;
    }

    public void SelecionarTudo() { foreach (Midia m in itens) m.Marcada = true; SelecaoAlterada(); }
    public void LimparSelecao() { foreach (Midia m in itens) m.Marcada = false; ancora = null; SelecaoAlterada(); }
    public void Alternar(Midia m) { m.Marcada = !m.Marcada; ancora = m; SelecaoAlterada(); }

    void SelecaoAlterada()
    {
        marcadas = 0;
        foreach (Midia m in itens) if (m.Marcada) marcadas++;
        Invalidate();
        if (SelecaoMudou != null) SelecaoMudou();
    }

    // Circulo de marcar, no canto de cima de cada miniatura
    static Rectangle Circulo(Rectangle r) { return new Rectangle(r.X + 10, r.Y + 10, 26, 26); }

    static Rectangle Retangulo(Point a, Point b)
    {
        return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    void ComecarLaco(Point p)
    {
        laco = true;
        lacoIni = new Point(desceu.X, desceu.Y + rolagem);
        antesDoLaco = new HashSet<Midia>();
        if ((ModifierKeys & (Keys.Control | Keys.Shift)) != 0)
            foreach (Midia m in itens) if (m.Marcada) antesDoLaco.Add(m);
        rolarLaco.Start();
        AtualizarLaco(p);
    }

    void AtualizarLaco(Point p)
    {
        lacoFim = new Point(Math.Max(0, Math.Min(Width, p.X)), Math.Max(0, Math.Min(Height, p.Y)) + rolagem);
        Rectangle r = Retangulo(lacoIni, lacoFim);
        foreach (Midia m in itens)
        {
            Rectangle c = m.Caixa;
            c.Height += AlturaTexto;
            m.Marcada = antesDoLaco.Contains(m) || r.IntersectsWith(c);
        }
        SelecaoAlterada();
    }

    // Arrastando perto da borda de cima ou de baixo: a grade rola sozinha
    void RolarNoLaco()
    {
        if (!laco) { rolarLaco.Stop(); return; }
        Point p = PointToClient(MousePosition);
        int d = p.Y < 50 ? -(50 - p.Y) : p.Y > Height - 50 ? p.Y - (Height - 50) : 0;
        if (d == 0) return;
        Rolar(Math.Max(-60, Math.Min(60, d)));
        AtualizarLaco(p);
    }

    void Organizar()
    {
        alturaVista = Height;
        int util = Math.Max(LarguraMin, Width - 2 * Margem);
        int colunas = Math.Max(1, (util + Espaco) / (LarguraMin + Espaco));
        larg = (util - (colunas - 1) * Espaco) / colunas;
        alt = larg * 9 / 16;
        dias.Clear();
        int y = 4, c = 0;
        DateTime dia = DateTime.MinValue;
        foreach (Midia m in itens)
        {
            if (m.Data.Date != dia)
            {
                if (c > 0) { y += alt + AlturaTexto + Espaco; c = 0; }
                dias.Add(new KeyValuePair<int, string>(y, NomeDia(m.Data)));
                y += AlturaDia;
                dia = m.Data.Date;
            }
            m.Caixa = new Rectangle(Margem + c * (larg + Espaco), y, larg, alt);
            if (++c == colunas) { c = 0; y += alt + AlturaTexto + Espaco; }
        }
        if (c > 0) y += alt + AlturaTexto + Espaco;
        total = y + 24;
        rolagem = Math.Max(0, Math.Min(rolagem, total - Height));
    }

    public static string NomeDia(DateTime d)
    {
        if (d.Date == DateTime.Today) return "Hoje";
        if (d.Date == DateTime.Today.AddDays(-1)) return "Ontem";
        string s = d.ToString("dddd, d 'de' MMMM", Br);
        if (d.Year != DateTime.Today.Year) s += " de " + d.Year;
        return char.ToUpper(s[0]) + s.Substring(1);
    }

    protected override void OnResize(EventArgs e) { Organizar(); Invalidate(); base.OnResize(e); }

    public void Rolar(int d)
    {
        int n = (int)Math.Max(0, Math.Min((long)rolagem + d, total - Height));
        if (n == rolagem) return;
        rolagem = n;
        AtualizarSobre(PointToClient(MousePosition));
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Rolar(-e.Delta * (alt + AlturaTexto + Espaco) / 240);
        base.OnMouseWheel(e);
    }

    Midia Achar(Point p)
    {
        Point q = new Point(p.X, p.Y + rolagem);
        foreach (Midia m in itens)
        {
            Rectangle r = m.Caixa;
            r.Height += AlturaTexto;
            if (r.Contains(q)) return m;
        }
        return null;
    }

    void AtualizarSobre(Point p)
    {
        Midia n = ClientRectangle.Contains(p) ? Achar(p) : null;
        if (n == sobre) return;
        sobre = n;
        Cursor = n != null ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    int AlturaPolegar { get { return Math.Max(48, Height * Height / Math.Max(1, total)); } }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left && total > Height && e.X >= Width - 22)
        {
            arrastando = true;
            arrasteY = e.Y;
            arrasteRol = rolagem;
            Capture = true;
        }
        else if (e.Button == MouseButtons.Left)
        {
            pendente = true;
            desceu = e.Location;
            Capture = true;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (arrastando)
        {
            int livre = Math.Max(1, Height - AlturaPolegar);
            rolagem = Math.Max(0, Math.Min(total - Height, arrasteRol + (e.Y - arrasteY) * (total - Height) / livre));
            Invalidate();
        }
        else
        {
            if (pendente && !laco && (Math.Abs(e.X - desceu.X) > 6 || Math.Abs(e.Y - desceu.Y) > 6)) ComecarLaco(e.Location);
            if (laco) AtualizarLaco(e.Location);
            else AtualizarSobre(e.Location);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
        if (arrastando) { arrastando = false; return; }
        if (laco) { laco = false; pendente = false; rolarLaco.Stop(); Invalidate(); return; }
        bool clicou = pendente;
        pendente = false;
        Midia m = Achar(e.Location);

        if (e.Button == MouseButtons.Right)
        {
            if (m != null && Clicou != null) Clicou(m, MouseButtons.Right, PointToScreen(e.Location));
            return;
        }
        if (e.Button != MouseButtons.Left || !clicou) return;
        Keys mod = ModifierKeys;
        if (m == null)
        {
            if ((mod & Keys.Control) == 0 && marcadas > 0) LimparSelecao();
            return;
        }
        Rectangle r = m.Caixa;
        r.Offset(0, -rolagem);
        if ((mod & Keys.Shift) != 0 && ancora != null && itens.Contains(ancora))
        {
            int a = itens.IndexOf(ancora), b = itens.IndexOf(m);
            if ((mod & Keys.Control) == 0) foreach (Midia x in itens) x.Marcada = false;
            for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) itens[i].Marcada = true;
            SelecaoAlterada();
            return;
        }
        if ((mod & Keys.Control) != 0 || marcadas > 0 || Circulo(r).Contains(e.Location))
        {
            m.Marcada = !m.Marcada;
            ancora = m;
            SelecaoAlterada();
            return;
        }
        ancora = m;
        if (Clicou != null) Clicou(m, MouseButtons.Left, PointToScreen(e.Location));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (sobre != null) { sobre = null; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.InterpolationMode = InterpolationMode.Bilinear;
        int topo = rolagem;
        foreach (KeyValuePair<int, string> d in dias)
            if (d.Key + AlturaDia > topo && d.Key < topo + Height)
                TextRenderer.DrawText(g, d.Value, Tema.Titulo, new Rectangle(Margem, d.Key - topo, 700, AlturaDia - 10), Tema.Texto, Tema.Linha);

        foreach (Midia m in itens)
        {
            Rectangle r = m.Caixa;
            r.Offset(0, -topo);
            if (r.Bottom + AlturaTexto < 0 || r.Top > Height) continue;
            if (e.ClipRectangle.IntersectsWith(new Rectangle(r.X, r.Y, r.Width, r.Height + AlturaTexto))) Desenhar(g, m, r);
            if (m.Mini == null && !m.Pedida) Pedir(m);
        }

        if (laco)
        {
            Rectangle r = Retangulo(lacoIni, lacoFim);
            r.Offset(0, -topo);
            g.SmoothingMode = SmoothingMode.None;
            g.FillRectangle(Tema.Pincel(Color.FromArgb(50, Tema.Destaque)), r);
            using (Pen pen = new Pen(Tema.Destaque)) g.DrawRectangle(pen, r);
        }

        if (total > Height)
        {
            int livre = Height - AlturaPolegar;
            int y = (int)((long)rolagem * livre / Math.Max(1, total - Height));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Tema.Arredondado(new Rectangle(Width - 14, y + 4, 6, AlturaPolegar - 8), 3))
                g.FillPath(Tema.Pincel(arrastando ? Tema.Suave : Tema.CampoSob), p);
        }
    }

    void Desenhar(Graphics g, Midia m, Rectangle r)
    {
        bool s = m == sobre;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Arredondado(r, 10))
        {
            g.FillPath(Tema.Pincel(Tema.Painel), p);
            if (m.Mini != null)
            {
                GraphicsState estado = g.Save();
                g.SetClip(p, CombineMode.Intersect);
                g.DrawImage(m.Mini, r);
                g.Restore(estado);
            }
            else
                TextRenderer.DrawText(g, m.Video ? "" : "", FonteVazia, r, Tema.CampoSob, Tema.Centro);
            if (m.Marcada) g.FillPath(Tema.Pincel(Color.FromArgb(70, Tema.Destaque)), p);
            if (s || m.Marcada) using (Pen pen = new Pen(Tema.Destaque, 3)) g.DrawPath(pen, p);
        }
        // Circulo de marcar: aparece no item sob o mouse ou quando ha selecao
        if (m.Marcada || s || marcadas > 0)
        {
            Rectangle c = Circulo(r);
            if (m.Marcada)
            {
                g.FillEllipse(Tema.Pincel(Tema.Destaque), c);
                TextRenderer.DrawText(g, "", FonteMarca, c, Color.White, Tema.Centro);
            }
            else
            {
                g.FillEllipse(Tema.Pincel(Color.FromArgb(120, 0, 0, 0)), c);
                using (Pen pen = new Pen(Color.FromArgb(230, 255, 255, 255), 2)) g.DrawEllipse(pen, c);
            }
        }
        if (m.Video)
        {
            string t = "▶  " + (m.Duracao ?? "clipe");
            Size ts = TextRenderer.MeasureText(t, Tema.Pequena);
            Rectangle b = new Rectangle(r.Right - ts.Width - 20, r.Bottom - 32, ts.Width + 10, 22);
            using (GraphicsPath p = Tema.Arredondado(b, 6)) g.FillPath(Tema.Pincel(Color.FromArgb(210, 0, 0, 0)), p);
            TextRenderer.DrawText(g, t, Tema.Pequena, b, Color.White, Tema.Centro);
        }
        g.SmoothingMode = SmoothingMode.None;
        TextRenderer.DrawText(g, m.Tipo, Tema.Negrito, new Rectangle(r.X + 2, r.Bottom + 8, r.Width - 74, 22),
            s || m.Marcada ? Tema.Destaque : Tema.Texto, Tema.Linha);
        TextRenderer.DrawText(g, m.Data.ToString("HH:mm"), Tema.Pequena, new Rectangle(r.Right - 72, r.Bottom + 8, 70, 22),
            Tema.Suave, Tema.Linha | TextFormatFlags.Right);
        TextRenderer.DrawText(g, Replay.Tamanho(m.Tamanho) + Tema.Ponto + Path.GetFileName(m.Caminho), Tema.Pequena,
            new Rectangle(r.X + 2, r.Bottom + 30, r.Width - 4, 20), Tema.Suave, Tema.Linha);
    }

    // ---- Miniaturas ----
    void Pedir(Midia m)
    {
        m.Pedida = true;
        lock (pedidos) pedidos.Push(m);
        temPedido.Set();
    }

    bool PertoDaTela(Midia m)
    {
        int topo = rolagem, h = alturaVista;
        return m.Caixa.Bottom + AlturaTexto > topo - h && m.Caixa.Top < topo + 2 * h;
    }

    void Carregador()
    {
        while (!parar)
        {
            temPedido.WaitOne(1000);
            while (!parar)
            {
                Midia m;
                lock (pedidos)
                {
                    if (pedidos.Count == 0) break;
                    m = pedidos.Pop();
                }
                if (!PertoDaTela(m)) { m.Pedida = false; continue; }   // rolou para longe
                string d = null;
                Bitmap b = null;
                try { b = Miniaturas.Ler(m.Caminho, m.Video, out d); }
                catch (Exception ex) { App.Log("Erro na miniatura de " + m.Caminho + ": " + ex.Message); }
                try { BeginInvoke((MethodInvoker)delegate { Entregar(m, b, d); }); }
                catch { if (b != null) b.Dispose(); return; }
            }
        }
    }

    void Entregar(Midia m, Bitmap b, string d)
    {
        if (parar || IsDisposed) { if (b != null) b.Dispose(); return; }
        if (m.Mini != null) m.Mini.Dispose();
        m.Mini = b;
        if (d != null) m.Duracao = d;

        // Memoria: solta as miniaturas que ficaram longe
        int carregadas = 0;
        foreach (Midia x in itens) if (x.Mini != null) carregadas++;
        if (carregadas > MaxMinis)
            foreach (Midia x in itens)
                if (x.Mini != null && Math.Abs(x.Caixa.Y - rolagem) > 3 * Height)
                {
                    x.Mini.Dispose();
                    x.Mini = null;
                    x.Pedida = false;
                }

        Rectangle r = m.Caixa;
        r.Offset(0, -rolagem);
        r.Height += AlturaTexto;
        Invalidate(r);
    }

    public void Parar()
    {
        parar = true;
        temPedido.Set();
    }

    protected override void Dispose(bool disposing)
    {
        Parar();
        if (disposing) rolarLaco.Dispose();
        base.Dispose(disposing);
    }
}

// Alt + Z: prints e clipes numa tela so, com o replay e as configuracoes
// do clipe no topo, como a sobreposicao da NVIDIA
class Galeria : Form
{
    public static Galeria Atual;
    public bool FicarAberta;    // testes: nao fecha ao perder o foco
    public bool SemConfirmar;   // testes: exclui sem perguntar
    public GradeMidias Grade { get { return grade; } }
    public void ExcluirSelecionadas(bool permanente) { Excluir(grade.Selecionadas(), permanente); }
    static readonly Color Fundo = Color.FromArgb(14, 14, 18);
    static readonly Font FonteTitulo = new Font("Segoe UI Semibold", 20f);

    string pastaPrints, pastaClipes;
    readonly IntPtr anterior;
    readonly List<Midia> todas = new List<Midia>();
    List<Midia> visiveis = new List<Midia>();
    int filtro;                 // 0 = tudo, 1 = prints, 2 = clipes
    bool emConfig, dialogo, abrindo;
    readonly GradeMidias grade;
    readonly Panel barra, filtros, corpo;
    Panel config;
    readonly Label[] chips = new Label[3];
    readonly Label vazio, pastaP, pastaC;
    readonly BotaoGrande btReplay, btSalvar, btGravar, btConfig;
    readonly ToolTip dicas = new ToolTip();
    readonly List<Action> atualizar = new List<Action>();
    readonly Nucleo nucleo;          // para gravar atalhos (null nos testes)
    int gravandoAtalho = -1;
    Label msgAtalho;
    readonly Panel barraSel;         // aparece no lugar dos filtros quando ha selecao
    readonly Label textoSel;

    public Galeria(Nucleo n) : this(Config.PastaPrints, ConfigClipe.Pasta, n) { }

    public Galeria(string prints, string clipes, Nucleo n = null)
    {
        pastaPrints = prints;
        pastaClipes = clipes;
        nucleo = n;
        if (nucleo != null) nucleo.TeclaGravada += TeclaGravada;
        anterior = Nativo.GetForegroundWindow();

        Text = "Galeria";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = Fundo;
        Bounds = Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;

        // ---- Barra de cima ----
        barra = new Panel();
        barra.Dock = DockStyle.Top;
        barra.Height = 124;
        barra.BackColor = Fundo;
        barra.Paint += PintarBarra;

        btReplay = new BotaoGrande("", "Replay instantâneo");
        btSalvar = new BotaoGrande("", "Salvar clipe");
        btGravar = new BotaoGrande("", "Gravar");
        btConfig = new BotaoGrande("", "Configurações");
        BotaoGrande[] bts = { btReplay, btSalvar, btGravar, btConfig };
        // Cabe numa linha ao lado do titulo? Senao (monitor em pe), desce para uma segunda linha
        int livre = Width - 300 - 110, largBt, x, yBt = 24;
        if (4 * 230 + 3 * 12 <= livre)
        {
            largBt = Math.Min(290, (livre - 3 * 12) / 4);
            x = Math.Max(300, (Width - (4 * largBt + 3 * 12)) / 2);
        }
        else
        {
            largBt = Math.Min(290, (Width - 2 * GradeMidias.Margem - 3 * 12) / 4);
            x = GradeMidias.Margem;
            yBt = 108;
            barra.Height = 204;
        }
        foreach (BotaoGrande b in bts) { b.Width = largBt; b.Location = new Point(x, yBt); x += b.Width + 12; barra.Controls.Add(b); }
        btReplay.Click += delegate { Replay.DefinirLigado(!ConfigClipe.Ligado); };
        btSalvar.Click += delegate { Replay.SalvarClipe(); };
        btGravar.Click += delegate
        {
            bool comecar = !Gravacao.Ativa;
            Gravacao.Alternar();
            if (comecar) Close();   // volta para o jogo
        };
        btConfig.Click += delegate { MostrarConfig(!emConfig); };
        dicas.SetToolTip(btGravar, "Grava até você parar (" + Config.Atalhos[Config.Gravar] + ")");
        dicas.SetToolTip(btReplay, "Liga ou desliga (" + Config.Atalhos[Config.AlternarReplay] + ")");
        dicas.SetToolTip(btSalvar, "Salva os últimos segundos (" + Config.Atalhos[Config.SalvarClipe] + ")");

        Label fechar = new Label();
        fechar.Text = "";
        fechar.Font = new Font("Segoe MDL2 Assets", 13f);
        fechar.ForeColor = Tema.Suave;
        fechar.BackColor = Fundo;
        fechar.TextAlign = ContentAlignment.MiddleCenter;
        fechar.Bounds = new Rectangle(Width - 92, 34, 56, 56);
        fechar.Cursor = Cursors.Hand;
        fechar.MouseEnter += delegate { fechar.BackColor = Tema.Fechar; fechar.ForeColor = Color.White; };
        fechar.MouseLeave += delegate { fechar.BackColor = Fundo; fechar.ForeColor = Tema.Suave; };
        fechar.MouseDown += delegate { Close(); };
        dicas.SetToolTip(fechar, "Fechar (Esc)");
        barra.Controls.Add(fechar);

        // ---- Filtros ----
        filtros = new Panel();
        filtros.Dock = DockStyle.Top;
        filtros.Height = 70;
        filtros.BackColor = Fundo;
        for (int i = 0; i < 3; i++)
        {
            Label c = new Label();
            c.Font = Tema.Negrito;
            c.TextAlign = ContentAlignment.MiddleCenter;
            c.UseMnemonic = false;
            c.Cursor = Cursors.Hand;
            int idx = i;
            c.MouseEnter += delegate { if (idx != filtro) c.BackColor = Tema.CampoSob; };
            c.MouseLeave += delegate { if (idx != filtro) c.BackColor = Tema.Campo; };
            c.MouseDown += delegate { filtro = idx; Filtrar(); };
            chips[i] = c;
            filtros.Controls.Add(c);
        }
        pastaC = LinkPasta("Pasta de clipes", delegate { return pastaClipes; });
        pastaP = LinkPasta("Pasta de prints", delegate { return pastaPrints; });
        pastaC.Location = new Point(Width - GradeMidias.Margem - pastaC.Width, 22);
        pastaP.Location = new Point(pastaC.Left - 16 - pastaP.Width, 22);
        filtros.Controls.Add(pastaP);
        filtros.Controls.Add(pastaC);

        // ---- Barra da selecao ----
        barraSel = new Panel();
        barraSel.Dock = DockStyle.Top;
        barraSel.Height = 70;
        barraSel.BackColor = Fundo;
        barraSel.Visible = false;
        textoSel = new Label();
        textoSel.Font = Tema.Titulo;
        textoSel.ForeColor = Tema.Texto;
        textoSel.BackColor = Fundo;
        textoSel.TextAlign = ContentAlignment.MiddleLeft;
        textoSel.Bounds = new Rectangle(GradeMidias.Margem, 16, 240, 38);
        barraSel.Controls.Add(textoSel);
        int xs = GradeMidias.Margem + 250;
        xs = BotaoSel(xs, "Copiar", Tema.Campo, delegate { Copiar(grade.Selecionadas()); });
        xs = BotaoSel(xs, "Mostrar na pasta", Tema.Campo, delegate { MostrarVarios(grade.Selecionadas()); });
        xs = BotaoSel(xs, "Mandar para a Lixeira", Tema.Campo, delegate { Excluir(grade.Selecionadas(), false); });
        xs = BotaoSel(xs, "Excluir permanentemente", Color.FromArgb(120, 36, 36), delegate { Excluir(grade.Selecionadas(), true); });
        xs = BotaoSel(xs + 16, "Selecionar tudo", Tema.Campo, delegate { grade.SelecionarTudo(); });
        BotaoSel(xs, "Cancelar", Tema.Campo, delegate { grade.LimparSelecao(); });

        // ---- Corpo ----
        corpo = new Panel();
        corpo.Dock = DockStyle.Fill;
        corpo.BackColor = Fundo;
        grade = new GradeMidias();
        grade.Dock = DockStyle.Fill;
        grade.BackColor = Fundo;
        grade.Clicou += Clicou;
        grade.SelecaoMudou += AtualizarSelecao;
        vazio = new Label();
        vazio.Dock = DockStyle.Fill;
        vazio.TextAlign = ContentAlignment.MiddleCenter;
        vazio.Font = Tema.Normal;
        vazio.ForeColor = Tema.Suave;
        vazio.BackColor = Fundo;
        vazio.Visible = false;
        corpo.Controls.Add(grade);
        corpo.Controls.Add(vazio);

        Controls.Add(corpo);
        Controls.Add(barraSel);
        Controls.Add(filtros);
        Controls.Add(barra);

        Replay.Mudou += AtualizarReplay;
        AtualizarReplay();
        Carregar();
    }

    // Nao aparece nos clipes nem nas gravacoes
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Capturas.Esconder(Handle); }

    int BotaoSel(int x, string texto, Color fundo, Action acao)
    {
        Label b = new Label();
        b.Text = texto;
        b.Font = Tema.Negrito;
        b.ForeColor = Tema.Texto;
        b.BackColor = fundo;
        b.UseMnemonic = false;
        b.TextAlign = ContentAlignment.MiddleCenter;
        b.Cursor = Cursors.Hand;
        int w = TextRenderer.MeasureText(texto, Tema.Negrito).Width + 32, y = 16;
        foreach (Control c in barraSel.Controls) y = Math.Max(y, c.Top);
        if (x + w > Width - GradeMidias.Margem)   // tela estreita: quebra a linha
        {
            x = GradeMidias.Margem;
            y += 46;
            barraSel.Height = y + 54;
        }
        b.Bounds = new Rectangle(x, y, w, 38);
        Color sob = ControlPaint.Light(fundo, 0.25f);
        b.MouseEnter += delegate { b.BackColor = sob; };
        b.MouseLeave += delegate { b.BackColor = fundo; };
        b.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) acao(); };
        barraSel.Controls.Add(b);
        return b.Right + 8;
    }

    void AtualizarSelecao()
    {
        int n = grade.Marcadas;
        textoSel.Text = n == 1 ? "1 selecionado" : n + " selecionados";
        AtualizarCorpo();
    }

    Label LinkPasta(string texto, Func<string> pasta)
    {
        Label l = new Label();
        l.Text = texto;
        l.Font = Tema.Pequena;
        l.ForeColor = Tema.Suave;
        l.BackColor = Fundo;
        l.Size = new Size(TextRenderer.MeasureText(texto, Tema.Pequena).Width + 8, 26);
        l.TextAlign = ContentAlignment.MiddleCenter;
        l.Cursor = Cursors.Hand;
        l.MouseEnter += delegate { l.ForeColor = Tema.Destaque; };
        l.MouseLeave += delegate { l.ForeColor = Tema.Suave; };
        l.MouseDown += delegate { abrindo = true; App.AbrirPasta(pasta()); Close(); };
        return l;
    }

    void PintarBarra(object s, PaintEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, "Galeria", FonteTitulo, new Point(44, 28), Tema.Texto);
        TextRenderer.DrawText(e.Graphics, Config.Nome + Tema.Ponto + "prints, clipes e gravações" + Tema.Ponto + Config.Atalhos[Config.AbrirGaleria], Tema.Pequena,
            new Point(48, 72), Tema.Suave);
        e.Graphics.FillRectangle(Tema.Pincel(Tema.Borda), GradeMidias.Margem, barra.Height - 1, barra.Width - 2 * GradeMidias.Margem, 1);
    }

    public void Mostrar()
    {
        Atual = this;
        Show();
        Activate();
        Nativo.SetForegroundWindow(Handle);
        grade.Focus();
    }

    // ---- Lista ----
    public void Recarregar() { Carregar(); }

    void Carregar()
    {
        Dictionary<string, Midia> antes = new Dictionary<string, Midia>(StringComparer.OrdinalIgnoreCase);
        foreach (Midia m in todas) antes[m.Caminho] = m;
        todas.Clear();
        HashSet<string> vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Listar(pastaPrints, false, vistos, antes, ".png", ".jpg", ".jpeg", ".bmp");
        Listar(pastaClipes, true, vistos, antes, ".mp4", ".mkv", ".mov");
        foreach (Midia m in antes.Values)
            if (!todas.Contains(m) && m.Mini != null) { m.Mini.Dispose(); m.Mini = null; }
        todas.Sort(delegate(Midia a, Midia b) { return b.Data.CompareTo(a.Data); });
        Filtrar();
    }

    void Listar(string pasta, bool video, HashSet<string> vistos, Dictionary<string, Midia> antes, params string[] tipos)
    {
        try
        {
            if (!Directory.Exists(pasta)) return;
            foreach (FileInfo f in new DirectoryInfo(pasta).EnumerateFiles())
            {
                if (Array.IndexOf(tipos, f.Extension.ToLowerInvariant()) < 0 || f.Length == 0) continue;
                if (!vistos.Add(f.FullName)) continue;
                Midia m;
                if (!antes.TryGetValue(f.FullName, out m) || m.Tamanho != f.Length || m.Data != f.LastWriteTime)
                {
                    m = new Midia();
                    m.Caminho = f.FullName;
                    m.Video = video;
                    m.Data = f.LastWriteTime;
                    m.Tamanho = f.Length;
                }
                todas.Add(m);
            }
        }
        catch (Exception ex) { App.Log("Erro ao listar " + pasta + ": " + ex.Message); }
    }

    void Filtrar()
    {
        int prints = 0, clipes = 0;
        foreach (Midia m in todas) if (m.Video) clipes++; else prints++;
        visiveis = new List<Midia>();
        foreach (Midia m in todas)
            if (filtro == 0 || (filtro == 2) == m.Video) visiveis.Add(m);

        string[] nomes = { "Tudo   " + todas.Count, "Prints   " + prints, "Clipes   " + clipes };
        int x = GradeMidias.Margem;
        for (int i = 0; i < 3; i++)
        {
            chips[i].Text = nomes[i];
            chips[i].Bounds = new Rectangle(x, 16, TextRenderer.MeasureText(nomes[i], Tema.Negrito).Width + 36, 38);
            chips[i].BackColor = i == filtro ? Tema.Destaque : Tema.Campo;
            chips[i].ForeColor = i == filtro ? Color.White : Tema.Texto;
            x += chips[i].Width + 8;
        }

        grade.Definir(visiveis);
        if (filtro == 1) vazio.Text = "Nenhum print ainda.\n" + Config.Atalhos[Config.PrintTela] + " tira um print da tela inteira.";
        else if (filtro == 2 || todas.Count == 0)
            vazio.Text = "Nenhum clipe ainda.\nCom o replay ligado, " + Config.Atalhos[Config.SalvarClipe] +
                         " salva os últimos " + ConfigClipe.NomeDuracao(ConfigClipe.Segundos) + " da tela.";
        AtualizarCorpo();
    }

    void AtualizarCorpo()
    {
        bool sel = !emConfig && grade.Marcadas > 0;
        barraSel.Visible = sel;
        filtros.Visible = !emConfig && !sel;
        grade.Visible = !emConfig && visiveis.Count > 0;
        vazio.Visible = !emConfig && visiveis.Count == 0;
        if (config != null) config.Visible = emConfig;
    }

    // ---- Acoes nos itens ----
    // Botao direito: vale para a selecao se o item estiver nela, senao so para ele
    void Clicou(Midia m, MouseButtons b, Point tela)
    {
        if (b == MouseButtons.Left) { Abrir(m); return; }
        if (b != MouseButtons.Right) return;
        List<Midia> alvo = m.Marcada ? grade.Selecionadas() : new List<Midia> { m };
        bool um = alvo.Count == 1;
        ContextMenuStrip menu = CoresMenu.NovoMenu();
        if (um)
        {
            ToolStripItem abrir = menu.Items.Add(m.Video ? "Assistir" : "Abrir no editor", null, delegate { Abrir(m); });
            abrir.Font = Tema.Negrito;
        }
        menu.Items.Add(um ? (m.Video ? "Copiar o arquivo" : "Copiar a imagem") : "Copiar " + alvo.Count + " arquivos", null, delegate { Copiar(alvo); });
        menu.Items.Add("Mostrar na pasta", null, delegate { MostrarVarios(alvo); });
        if (!m.Marcada) menu.Items.Add("Selecionar", null, delegate { grade.Alternar(m); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(um ? "Mandar para a Lixeira" : "Mandar " + alvo.Count + " para a Lixeira", null, delegate { Excluir(alvo, false); });
        ToolStripItem perm = menu.Items.Add(um ? "Excluir permanentemente" : "Excluir " + alvo.Count + " permanentemente", null, delegate { Excluir(alvo, true); });
        perm.ForeColor = Tema.Perigo;
        menu.Closed += delegate { BeginInvoke((MethodInvoker)delegate { menu.Dispose(); }); };
        menu.Show(tela);
    }

    void Abrir(Midia m)
    {
        if (!File.Exists(m.Caminho)) { Carregar(); return; }
        abrindo = true;
        Close();
        try
        {
            if (m.Video)
            {
                ProcessStartInfo i = new ProcessStartInfo(m.Caminho);
                i.UseShellExecute = true;
                Process.Start(i);
            }
            else App.Editar(Historico.LerImagem(m.Caminho));
        }
        catch (Exception ex)
        {
            App.Log("Erro ao abrir " + m.Caminho + ": " + ex.Message);
            MostrarNaPasta(m.Caminho);
        }
    }

    static void MostrarNaPasta(string arq)
    {
        try { Process.Start("explorer.exe", "/select,\"" + arq + "\""); }
        catch (Exception ex) { App.Log("Erro ao mostrar na pasta: " + ex.Message); }
    }

    // Uma janela do Explorer por pasta (prints e clipes ficam em pastas diferentes)
    void MostrarVarios(List<Midia> alvo)
    {
        if (alvo.Count == 0) return;
        abrindo = true;
        HashSet<string> pastas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Midia m in alvo)
            if (pastas.Add(Path.GetDirectoryName(m.Caminho))) MostrarNaPasta(m.Caminho);
        Close();
    }

    // Uma imagem vai como imagem (cola em qualquer lugar); o resto vai como arquivos
    void Copiar(List<Midia> alvo)
    {
        if (alvo.Count == 0) return;
        try
        {
            if (alvo.Count == 1 && !alvo[0].Video)
                using (Bitmap b = Historico.LerImagem(alvo[0].Caminho)) App.PorImagem(b);
            else
            {
                System.Collections.Specialized.StringCollection l = new System.Collections.Specialized.StringCollection();
                foreach (Midia m in alvo) l.Add(m.Caminho);
                Clipboard.SetFileDropList(l);
            }
            Aviso.Mostrar(alvo.Count == 1 ? alvo[0].Tipo + " copiado" : alvo.Count + " arquivos copiados",
                "Cole com Ctrl + V (no WhatsApp, Discord, numa pasta...)", Tema.Sucesso);
        }
        catch (Exception ex) { App.Log("Erro ao copiar da galeria: " + ex.Message); }
    }

    void Excluir(List<Midia> alvo, bool permanente)
    {
        if (alvo.Count == 0) return;
        dialogo = true;
        try
        {
            if (permanente && !SemConfirmar)
            {
                string q = alvo.Count == 1 ? "\"" + Path.GetFileName(alvo[0].Caminho) + "\"" : alvo.Count + " itens";
                if (MessageBox.Show(this, "Excluir " + q + " para sempre?\n\nN\u00e3o vai para a Lixeira e n\u00e3o d\u00e1 para desfazer.",
                        Config.Nome, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }
            int erros = 0;
            foreach (Midia m in alvo)
            {
                try
                {
                    if (permanente) File.Delete(m.Caminho);
                    else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(m.Caminho,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                catch (Exception ex) { erros++; App.Log("Erro ao excluir " + m.Caminho + ": " + ex.Message); }
            }
            int feitos = alvo.Count - erros;
            if (feitos > 0)
                Aviso.Mostrar((feitos == 1 ? "1 item " : feitos + " itens ") + (permanente ? "exclu\u00eddo" + (feitos == 1 ? "" : "s") + " para sempre" : "na Lixeira"),
                    erros > 0 ? erros + " n\u00e3o deu (arquivo em uso?)" : permanente ? "" : "D\u00e1 para restaurar pela Lixeira do Windows", permanente ? Tema.Perigo : Tema.Sucesso);
        }
        finally { dialogo = false; }
        Activate();
        Carregar();
    }

    // ---- Estado do replay ----
    void AtualizarReplay()
    {
        if (IsDisposed) return;
        string sub;
        bool aceso = false;
        if (!ConfigClipe.Ligado) sub = "Desligado" + Tema.Ponto + "clique para ligar";
        else if (Replay.Gravando) { sub = "Gravando" + Tema.Ponto + "últimos " + ConfigClipe.NomeDuracao(Replay.SegundosAtuais); aceso = true; }
        else if (Replay.Iniciando) sub = "Iniciando...";
        else sub = "Com problema" + Tema.Ponto + "veja nas configurações";
        btReplay.Definir(sub, ConfigClipe.Ligado, aceso);
        btSalvar.Definir(Replay.Salvando ? "Salvando..." : "Últimos " + ConfigClipe.NomeDuracao(ConfigClipe.Segundos) +
                         Tema.Ponto + Config.Atalhos[Config.SalvarClipe], false, false);
        string subGravar;
        if (Gravacao.Ativa) subGravar = "Gravando " + Gravacao.Tempo + Tema.Ponto + "clique para parar";
        else if (Gravacao.Iniciando) subGravar = "Iniciando...";
        else if (Gravacao.Parando) subGravar = "Salvando a gravação...";
        else subGravar = "Até você parar" + Tema.Ponto + Config.Atalhos[Config.Gravar];
        btGravar.Definir(subGravar, Gravacao.Ativa, Gravacao.Ativa);
        btConfig.Definir("Qualidade, FPS, som, atalhos", emConfig, false);
        foreach (Action a in atualizar) a();
    }

    // ---- Configuracoes do clipe ----
    public void MostrarConfig(bool sim)
    {
        emConfig = sim;
        if (sim && config == null) CriarConfig();
        AtualizarCorpo();
        AtualizarReplay();
        if (sim) { Replay.TestarCodecs(); config.Focus(); }
        else grade.Focus();
    }

    int x0, xv;   // colunas: rotulo e valor
    const int LarguraConfig = 1040;
    int Direita { get { return Math.Min(x0 + LarguraConfig, Width - 40); } }   // limite sem rolagem para o lado
    int LarguraNota { get { return Direita - xv; } }

    Label Texto(int x, int y, int w, int h, string t, Font f, Color c)
    {
        Label l = new Label();
        l.Text = t;
        l.Font = f;
        l.ForeColor = c;
        l.BackColor = Fundo;
        l.UseMnemonic = false;
        l.Bounds = new Rectangle(x, y, w, h);
        l.TextAlign = ContentAlignment.MiddleLeft;
        config.Controls.Add(l);
        return l;
    }

    Label Botao(int x, int y, string t)
    {
        Label l = Texto(x, y, TextRenderer.MeasureText(t, Tema.Pequena).Width + 30, 34, t, Tema.Pequena, Tema.Texto);
        l.BackColor = Tema.Campo;
        l.TextAlign = ContentAlignment.MiddleCenter;
        l.Cursor = Cursors.Hand;
        l.MouseEnter += delegate { l.BackColor = Tema.CampoSob; };
        l.MouseLeave += delegate { l.BackColor = Tema.Campo; };
        return l;
    }

    void Secao(ref int y, string t)
    {
        y += 14;
        Texto(x0, y, 600, 34, t, Tema.Titulo, Tema.Texto);
        y += 44;
    }

    void Nota(ref int y, Func<string> texto, Func<Color> cor)
    {
        Label l = Texto(xv, y - 6, LarguraNota, 22, "", Tema.Pequena, Tema.Suave);
        l.AutoEllipsis = true;
        atualizar.Add(delegate { l.Text = texto(); if (cor != null) l.ForeColor = cor(); });
        y += 26;
    }

    // Mudou algo: grava, reinicia o replay (com pausa) e redesenha
    void Mudou()
    {
        ConfigClipe.Salvar();
        Replay.Reconfigurar();
        AtualizarReplay();
    }

    void Opcoes(ref int y, string rotulo, string[] nomes, Func<int> atual, Action<int> escolher, Func<int, bool> pode)
    {
        Texto(x0, y, xv - x0 - 10, 34, rotulo, Tema.Normal, Tema.Texto);
        int x = xv;
        Label[] bs = new Label[nomes.Length];
        for (int i = 0; i < nomes.Length; i++)
        {
            int w = TextRenderer.MeasureText(nomes[i], Tema.Pequena).Width + 28;
            if (x + w > Direita) { x = xv; y += 40; }
            Label b = Texto(x, y, w, 34, nomes[i], Tema.Pequena, Tema.Texto);
            b.TextAlign = ContentAlignment.MiddleCenter;
            x += w + 6;
            int idx = i;
            b.MouseEnter += delegate { if (idx != atual() && (pode == null || pode(idx))) b.BackColor = Tema.CampoSob; };
            b.MouseLeave += delegate { if (idx != atual()) b.BackColor = Tema.Campo; };
            b.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || idx == atual() || (pode != null && !pode(idx))) return;
                escolher(idx);
                Mudou();
            };
            bs[i] = b;
        }
        atualizar.Add(delegate
        {
            int a = atual();
            for (int i = 0; i < bs.Length; i++)
            {
                bool ok = pode == null || pode(i);
                bs[i].BackColor = i == a ? Tema.Destaque : Tema.Campo;
                bs[i].ForeColor = i == a ? Color.White : ok ? Tema.Texto : Color.FromArgb(96, 96, 108);
                bs[i].Cursor = ok && i != a ? Cursors.Hand : Cursors.Default;
            }
        });
        y += 46;
    }

    void Chave(ref int y, string rotulo, Func<bool> atual, Action<bool> mudar)
    {
        Texto(x0, y, xv - x0 - 10, 34, rotulo, Tema.Normal, Tema.Texto);
        Interruptor o = new Interruptor();
        o.BackColor = Fundo;
        o.Location = new Point(xv, y + 4);
        o.Mudou += delegate { mudar(o.Marcado); };
        config.Controls.Add(o);
        atualizar.Add(delegate { o.Marcado = atual(); });
        y += 44;
    }

    void CriarConfig()
    {
        config = new Panel();
        config.Dock = DockStyle.Fill;
        config.BackColor = Fundo;
        config.AutoScroll = true;
        corpo.Controls.Add(config);
        config.BringToFront();
        x0 = Math.Max(GradeMidias.Margem, (Width - LarguraConfig) / 2);
        xv = x0 + 280;
        int y = 4;

        Secao(ref y, "Replay instantâneo");
        Chave(ref y, "Gravar sempre os últimos segundos", delegate { return ConfigClipe.Ligado; }, delegate(bool v) { Replay.DefinirLigado(v); });
        Nota(ref y, delegate { return "Agora: " + Replay.StatusLongo; },
             delegate { return ConfigClipe.Ligado && !Replay.Gravando && !Replay.Iniciando ? Tema.Perigo : Tema.Suave; });

        string[] dur = new string[ConfigClipe.Duracoes.Length];
        for (int i = 0; i < dur.Length; i++) dur[i] = ConfigClipe.NomeDuracao(ConfigClipe.Duracoes[i]);
        Opcoes(ref y, "Duração do clipe", dur, delegate { return Array.IndexOf(ConfigClipe.Duracoes, ConfigClipe.Segundos); },
               delegate(int i) { ConfigClipe.Segundos = ConfigClipe.Duracoes[i]; }, null);

        SecaoAtalhos(ref y);

        Secao(ref y, "Vídeo");
        Opcoes(ref y, "Qualidade", ConfigClipe.NomesQualidade, ConfigClipe.Qualidade, ConfigClipe.UsarQualidade,
               delegate(int i) { return i < 3; });
        Nota(ref y, delegate { return "Baixa: 720p 30 FPS 10 Mbps" + Tema.Ponto + "Média: 1080p 60 FPS 20 Mbps" + Tema.Ponto + "Alta: nativa 60 FPS 50 Mbps"; }, null);

        Rectangle tela = Replay.MonitorEscolhido().Bounds;
        string[] res = new string[ConfigClipe.Alturas.Length];
        for (int i = 0; i < res.Length; i++)
            res[i] = ConfigClipe.Alturas[i] == 0 ? "Nativa (" + tela.Width + " × " + tela.Height + ")" : ConfigClipe.NomeAltura(ConfigClipe.Alturas[i]);
        Opcoes(ref y, "Resolução", res, delegate { return Array.IndexOf(ConfigClipe.Alturas, ConfigClipe.Altura); },
               delegate(int i) { ConfigClipe.Altura = ConfigClipe.Alturas[i]; }, null);
        Nota(ref y, delegate
        {
            return ConfigClipe.Altura == 0 ? "Nativa é a mais leve: a imagem nem passa pelo processador."
                                           : "Reduzir a resolução usa um pouco do processador para redimensionar a imagem.";
        }, null);

        string[] fps = new string[ConfigClipe.Quadros.Length];
        for (int i = 0; i < fps.Length; i++) fps[i] = ConfigClipe.Quadros[i] + " FPS";
        Opcoes(ref y, "Taxa de quadros", fps, delegate { return Array.IndexOf(ConfigClipe.Quadros, ConfigClipe.Fps); },
               delegate(int i) { ConfigClipe.Fps = ConfigClipe.Quadros[i]; }, null);

        Texto(x0, y, xv - x0 - 10, 34, "Taxa de bits", Tema.Normal, Tema.Texto);
        Deslizador d = new Deslizador();
        d.Minimo = ConfigClipe.MbpsMin;
        d.Maximo = ConfigClipe.MbpsMax;
        d.BackColor = Fundo;
        d.Location = new Point(xv - 10, y + 2);
        d.Size = new Size(440, 30);
        config.Controls.Add(d);
        Label valor = Texto(xv + 444, y, 140, 34, "", Tema.Negrito, Tema.Texto);
        d.Mexeu += delegate { ConfigClipe.Mbps = d.Valor; AtualizarReplay(); };
        d.Soltou += delegate { Mudou(); };
        atualizar.Add(delegate { d.Valor = ConfigClipe.Mbps; valor.Text = ConfigClipe.Mbps + " Mbps"; });
        y += 46;
        Nota(ref y, delegate
        {
            return "≈ " + (ConfigClipe.Mbps * 60 / 8) + " MB por minuto de clipe" + Tema.Ponto + "mais bits, mais qualidade e arquivo maior";
        }, null);

        Opcoes(ref y, "Formato", ConfigClipe.NomesCodecs, delegate { return Array.IndexOf(ConfigClipe.Codecs, ConfigClipe.Codec); },
               delegate(int i) { ConfigClipe.Codec = ConfigClipe.Codecs[i]; },
               delegate(int i) { return Replay.Disponivel(ConfigClipe.Codecs[i]) != false; });
        Nota(ref y, delegate
        {
            string s = "H.264 abre em qualquer lugar (WhatsApp, Discord). HEVC: arquivo menor, mesma qualidade.";
            foreach (string c in ConfigClipe.Codecs)
                if (Replay.Disponivel(c) == false) { s += " Apagado: a placa não grava."; break; }
            return s;
        }, null);

        Screen[] telas = Screen.AllScreens;
        string[] mons = new string[telas.Length + 1];
        mons[0] = "Principal";
        for (int i = 0; i < telas.Length; i++)
            mons[i + 1] = "Monitor " + (i + 1) + " (" + telas[i].Bounds.Width + " × " + telas[i].Bounds.Height + ")";
        Opcoes(ref y, "Monitor", mons, delegate
        {
            for (int i = 0; i < telas.Length; i++) if (telas[i].DeviceName == ConfigClipe.Monitor) return i + 1;
            return 0;
        }, delegate(int i) { ConfigClipe.Monitor = i == 0 ? "" : telas[i - 1].DeviceName; }, null);

        Secao(ref y, "Som");
        Chave(ref y, "Som do PC (jogo, música, chamada)", delegate { return ConfigClipe.SomPc; },
              delegate(bool v) { ConfigClipe.SomPc = v; Mudou(); });
        Chave(ref y, "Microfone", delegate { return ConfigClipe.Microfone; },
              delegate(bool v) { ConfigClipe.Microfone = v; Mudou(); });

        Secao(ref y, "Outros");
        Chave(ref y, "Mostrar o cursor do mouse", delegate { return ConfigClipe.MostrarCursor; },
              delegate(bool v) { ConfigClipe.MostrarCursor = v; Mudou(); });

        Texto(x0, y, xv - x0 - 10, 34, "Pasta dos clipes", Tema.Normal, Tema.Texto);
        Label pasta = Texto(xv, y, 480, 34, "", Tema.Pequena, Tema.Suave);
        pasta.AutoEllipsis = true;
        pasta.Cursor = Cursors.Hand;
        pasta.MouseEnter += delegate { pasta.ForeColor = Tema.Destaque; };
        pasta.MouseLeave += delegate { pasta.ForeColor = Tema.Suave; };
        pasta.MouseDown += delegate { abrindo = true; App.AbrirPasta(ConfigClipe.Pasta); Close(); };
        Label mudar = Botao(xv + 490, y, "Mudar...");
        mudar.MouseDown += delegate { EscolherPasta(); };
        Label padrao = Botao(mudar.Right + 6, y, "Padrão");
        padrao.MouseDown += delegate { ConfigClipe.Pasta = ConfigClipe.PastaPadrao; pastaClipes = ConfigClipe.Pasta; ConfigClipe.Salvar(); Carregar(); AtualizarReplay(); };
        atualizar.Add(delegate { pasta.Text = ConfigClipe.Pasta; padrao.Visible = ConfigClipe.Pasta != ConfigClipe.PastaPadrao; });
        y += 44;
        Nota(ref y, delegate
        {
            return "O replay ocupa até " + ConfigClipe.EspacoAnel() + " MB do disco, sempre reaproveitados (" + ConfigClipe.PastaAnel + ")";
        }, null);

        Texto(x0, y, xv - x0 - 10, 34, "ffmpeg", Tema.Normal, Tema.Texto);
        Label ff = Texto(xv, y, 480, 34, "", Tema.Pequena, Tema.Suave);
        ff.AutoEllipsis = true;
        Label baixar = Botao(xv + 490, y, "Baixar o ffmpeg (115 MB)");
        baixar.MouseDown += delegate { Replay.BaixarFfmpeg(); };
        atualizar.Add(delegate
        {
            string c = Ffmpeg.Caminho;
            if (Replay.ProgressoDownload >= 0) ff.Text = "Baixando... " + Replay.ProgressoDownload + "%";
            else ff.Text = c ?? "Não encontrado. O replay precisa dele para gravar a tela.";
            ff.ForeColor = c == null && Replay.ProgressoDownload < 0 ? Tema.Perigo : Tema.Suave;
            baixar.Visible = c == null && Replay.ProgressoDownload < 0;
        });
        y += 44;

        Texto(x0, y + 20, 10, 20, "", Tema.Pequena, Tema.Suave);   // margem no fim da rolagem
    }

    // ---- Atalhos dos clipes: clique na caixa e aperte a combinacao ----
    static readonly int[] AtalhosDaGaleria = { Config.SalvarClipe, Config.Gravar, Config.AbrirGaleria, Config.AlternarReplay };
    readonly Label[] caixasAtalho = new Label[4];

    void SecaoAtalhos(ref int y)
    {
        Secao(ref y, "Atalhos");
        for (int i = 0; i < AtalhosDaGaleria.Length; i++)
        {
            int acao = AtalhosDaGaleria[i];
            Texto(x0, y, xv - x0 - 10, 34, Config.NomesAcoes[acao], Tema.Normal, Tema.Texto);
            Label caixa = Texto(xv, y, 280, 34, "", Tema.Negrito, Tema.Texto);
            caixa.BackColor = Tema.Campo;
            caixa.TextAlign = ContentAlignment.MiddleCenter;
            caixa.Cursor = nucleo != null ? Cursors.Hand : Cursors.Default;
            caixa.MouseEnter += delegate { if (gravandoAtalho != acao) caixa.BackColor = Tema.CampoSob; };
            caixa.MouseLeave += delegate { caixa.BackColor = Tema.Campo; };
            caixa.MouseDown += delegate { ComecarAtalho(acao); };
            dicas.SetToolTip(caixa, "Clique e aperte a nova combinação");
            caixasAtalho[i] = caixa;
            y += 42;
        }
        atualizar.Add(MostrarAtalhos);
        msgAtalho = Texto(xv, y - 4, LarguraNota, 22,
            "Clique numa caixa e aperte a combinação" + Tema.Ponto + "Esc cancela" + Tema.Ponto + "Backspace remove",
            Tema.Pequena, Tema.Suave);
        y += 24;
        Label restaurar = Botao(xv, y, "Restaurar os padrões (Alt + F10, Alt + F9, Alt + Z, Alt + Shift + F10)");
        restaurar.MouseDown += delegate
        {
            PararAtalho();
            foreach (int a in AtalhosDaGaleria) Config.Atalhos[a] = Config.Padrao[a];
            // Se algum outro atalho ja usava a mesma combinacao, ele fica sem
            for (int j = 0; j < Config.Atalhos.Length; j++)
                if (Array.IndexOf(AtalhosDaGaleria, j) < 0)
                    foreach (int a in AtalhosDaGaleria)
                        if (Config.Atalhos[j].Igual(Config.Atalhos[a])) Config.Atalhos[j] = new Atalho(0, 0);
            SalvarAtalhos("Atalhos padrão restaurados.", false);
        };
        y += 48;
    }

    void MostrarAtalhos()
    {
        for (int i = 0; i < AtalhosDaGaleria.Length; i++)
        {
            if (caixasAtalho[i] == null) continue;
            int acao = AtalhosDaGaleria[i];
            if (acao == gravandoAtalho) { caixasAtalho[i].Text = "Aperte as teclas..."; caixasAtalho[i].ForeColor = Tema.Destaque; continue; }
            Atalho a = Config.Atalhos[acao];
            caixasAtalho[i].Text = a.ToString();
            caixasAtalho[i].ForeColor = a.Vazio ? Tema.Suave : Tema.Texto;
        }
    }

    void ComecarAtalho(int acao)
    {
        if (nucleo == null) return;
        gravandoAtalho = acao;
        nucleo.Gravando = true;
        Mensagem("", false);
        MostrarAtalhos();
    }

    void PararAtalho()
    {
        gravandoAtalho = -1;
        if (nucleo != null) nucleo.Gravando = false;
        MostrarAtalhos();
    }

    void Mensagem(string t, bool erro)
    {
        if (msgAtalho == null) return;
        msgAtalho.Text = t;
        msgAtalho.ForeColor = erro ? Tema.Perigo : Tema.Sucesso;
    }

    // Vem do gancho de teclado (que esconde a tecla do resto do Windows)
    void TeclaGravada(Atalho a)
    {
        if (gravandoAtalho < 0 || IsDisposed) return;
        int i = gravandoAtalho;
        if (a.Vk == 0x1B && a.Mods == 0) { PararAtalho(); return; }                          // Esc
        if (a.Vk == 0x08 && a.Mods == 0) { Config.Atalhos[i] = new Atalho(0, 0); SalvarAtalhos("Atalho removido.", false); return; }
        string problema = Config.ProblemaDoAtalho(i, a);
        if (problema != null) { PararAtalho(); Mensagem(problema, true); return; }
        Config.Atalhos[i] = a;
        SalvarAtalhos("Salvo: " + Config.NomesAcoes[i] + " = " + a, false);
    }

    void SalvarAtalhos(string msg, bool erro)
    {
        gravandoAtalho = -1;
        if (nucleo != null) { nucleo.Gravando = false; nucleo.RegistrarReservas(); }
        Config.Salvar();
        AtualizarReplay();
        Mensagem(msg, erro);
    }

    void EscolherPasta()
    {
        dialogo = true;
        try
        {
            using (FolderBrowserDialog f = new FolderBrowserDialog())
            {
                f.Description = "Onde salvar os clipes";
                f.SelectedPath = ConfigClipe.Pasta;
                if (f.ShowDialog(this) == DialogResult.OK && f.SelectedPath.Length > 0)
                {
                    ConfigClipe.Pasta = f.SelectedPath;
                    pastaClipes = ConfigClipe.Pasta;
                    ConfigClipe.Salvar();
                    Carregar();
                }
            }
        }
        finally { dialogo = false; }
        Activate();
        AtualizarReplay();
    }

    // ---- Teclado e ciclo de vida ----
    protected override bool ProcessCmdKey(ref Message msg, Keys k)
    {
        if (k == Keys.Escape)
        {
            if (emConfig) MostrarConfig(false);
            else if (grade.Marcadas > 0) grade.LimparSelecao();
            else Close();
            return true;
        }
        if (!emConfig)
        {
            switch (k)
            {
                case Keys.Control | Keys.A: grade.SelecionarTudo(); return true;
                case Keys.Control | Keys.C: if (grade.Marcadas > 0) Copiar(grade.Selecionadas()); return true;
                case Keys.Delete:           if (grade.Marcadas > 0) Excluir(grade.Selecionadas(), false); return true;
                case Keys.Shift | Keys.Delete: if (grade.Marcadas > 0) Excluir(grade.Selecionadas(), true); return true;
                case Keys.PageDown: grade.Rolar(grade.Height - 120); return true;
                case Keys.PageUp:   grade.Rolar(-(grade.Height - 120)); return true;
                case Keys.Down:     grade.Rolar(120); return true;
                case Keys.Up:       grade.Rolar(-120); return true;
                case Keys.Home:     grade.Rolar(-1000000000); return true;
                case Keys.End:      grade.Rolar(1000000000); return true;
                case Keys.F5:       Carregar(); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, k);
    }

    // Clicou em outro monitor ou trocou de janela: fecha, como a da NVIDIA
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (dialogo || abrindo || FicarAberta) return;
        BeginInvoke((MethodInvoker)delegate
        {
            if (!IsDisposed && !dialogo && !abrindo && Form.ActiveForm != this) Close();
        });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Replay.Mudou -= AtualizarReplay;
        if (nucleo != null)
        {
            nucleo.TeclaGravada -= TeclaGravada;
            if (gravandoAtalho >= 0) nucleo.Gravando = false;
        }
        if (Atual == this) Atual = null;
        grade.Parar();
        foreach (Midia m in todas)
            if (m.Mini != null) { m.Mini.Dispose(); m.Mini = null; }
        if (!abrindo && anterior != IntPtr.Zero) Nativo.SetForegroundWindow(anterior);
        dicas.Dispose();
        base.OnFormClosed(e);
        App.Aparar();
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
