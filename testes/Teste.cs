// Teste offline do ClipHistorico: compilado so com /define:TESTE, nunca entra no app.
#if TESTE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

static class Teste
{
    static void Foto(Janela j, string saida, string nome)
    {
        Application.DoEvents();
        Stopwatch sw = Stopwatch.StartNew();
        using (Bitmap b = new Bitmap(j.Width, j.Height)) { j.DrawToBitmap(b, new Rectangle(0, 0, j.Width, j.Height)); Console.WriteLine("desenhar " + nome + ": " + sw.ElapsedMilliseconds + " ms"); b.Save(Path.Combine(saida, nome + ".png"), ImageFormat.Png); }
    }

    static ListaCartoes AcharLista(Control c)
    {
        foreach (Control f in c.Controls)
        {
            ListaCartoes l = f as ListaCartoes;
            if (l != null && l.Visible) return l;
            l = AcharLista(f);
            if (l != null) return l;
        }
        return null;
    }

    static string AcharRodape(Control c)
    {
        foreach (Control f in c.Controls)
        {
            if (f is Label && f.Text.StartsWith("Clique  ")) return f.Text;
            string r = AcharRodape(f);
            if (r != null) return r;
        }
        return null;
    }

    static int falhas;
    static void Ok(bool c, string nome)
    {
        Console.WriteLine((c ? "ok    " : "FALHOU ") + nome);
        if (!c) falhas++;
    }

    public static void Rodar(string[] args)
    {
        string saida = args[0];
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // arquivos do historico
        Ok(Historico.EhDoHistorico(@"C:\x\20261105_002426867.png"), "png do historico");
        Ok(Historico.EhDoHistorico(@"C:\x\20261105_002545339.txt"), "txt do historico");
        foreach (string n in new[] { "ClipHistorico.exe", "ClipHistorico.cs", "compilar.bat", "emojis.gz", "icone.ico", "ClipHistorico.exe.config", "notas.txt", "foto.png", "20261105.txt" })
            Ok(!Historico.EhDoHistorico(@"C:\x\" + n), "nao apaga " + n);

        // limpar de verdade numa pasta temporaria, com a mesma regra
        string tmp = Path.Combine(saida, "pasta-limpar");
        Directory.CreateDirectory(tmp);
        foreach (string n in new[] { "ClipHistorico.cs", "compilar.bat", "20261105_002426867.png", "20261105_002545339.txt" }) File.WriteAllText(Path.Combine(tmp, n), "x");
        foreach (string f in Directory.GetFiles(tmp)) if (Historico.EhDoHistorico(f)) File.Delete(f);
        Ok(File.Exists(Path.Combine(tmp, "ClipHistorico.cs")) && File.Exists(Path.Combine(tmp, "compilar.bat")) && !File.Exists(Path.Combine(tmp, "20261105_002426867.png")), "limpar preserva o codigo");
        Directory.Delete(tmp, true);

        // titulo sem Regex
        Ok(Tema.TituloDe("\n\n  ola   mundo\t\ttudo \n bem") == "ola mundo tudo", "titulo junta espacos");
        Ok(Tema.TituloDe(new string('a', 500)).Length == 200, "titulo limita 200");

        // salvar imagem como
        string png = Path.Combine(saida, "orig.png");
        using (Bitmap b = new Bitmap(320, 200, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.Transparent); g.FillEllipse(Brushes.Red, 20, 20, 160, 160); }
            b.Save(png, ImageFormat.Png);
        }
        int w, h;
        Ok(Item.TamanhoDoPng(png, out w, out h) && w == 320 && h == 200, "tamanho lido do cabecalho");
        Item it = Item.DeArquivo(png);
        Ok(it.Info == "320 x 200" && it.Mini != null && it.Mini.Width == 56, "item por cabecalho e miniatura sob demanda");
        foreach (string ext in new[] { ".png", ".jpg", ".bmp" })
        {
            string d = Path.Combine(saida, "salvo" + ext);
            if (File.Exists(d)) File.Delete(d);
            Historico.SalvarComo(it, d);
            using (Image lida = Image.FromFile(d))
            {
                Ok(lida.Width == 320 && lida.Height == 200, "salvar como " + ext);
                if (ext == ".png") Ok(lida.RawFormat.Guid == ImageFormat.Png.Guid, "formato png");
                if (ext == ".jpg") Ok(lida.RawFormat.Guid == ImageFormat.Jpeg.Guid, "formato jpg");
                if (ext == ".bmp") Ok(lida.RawFormat.Guid == ImageFormat.Bmp.Guid, "formato bmp");
            }
        }
        using (Bitmap p = Historico.LerPrevia(png, 100, 100)) Ok(p.Width == 100 && p.Height == 62, "previa reduzida");

        // prazo do historico
        Historico.Itens.Clear();
        string velho = Path.Combine(saida, "20261101_100000000.txt");
        string novo = Path.Combine(saida, "20261105_100000000.txt");
        File.WriteAllText(velho, "velho"); File.WriteAllText(novo, "novo");
        Historico.Itens.Add(Item.DeTexto(novo, "novo"));
        Historico.Itens.Add(Item.DeTexto(velho, "velho"));
        DateTime agora = new DateTime(2026, 11, 5, 12, 0, 0);
        Config.PrazoHoras = 0;
        Ok(Historico.Expirar(agora) == 0 && !Historico.ProximoVencimento().HasValue, "Nunca nao apaga");
        Config.PrazoHoras = 168;
        Ok(Historico.Expirar(agora) == 0 && Historico.Itens.Count == 2, "7 dias mantem item de 4 dias");
        Config.PrazoHoras = 24;
        Ok(Historico.ProximoVencimento() == new DateTime(2026, 11, 2, 10, 0, 0), "proximo vencimento");
        Ok(Historico.Expirar(agora) == 1 && !File.Exists(velho) && File.Exists(novo), "1 dia apaga so o vencido");
        Ok(Historico.Itens.Count == 1 && Historico.Itens[0].Texto == "novo", "item novo continua");
        Historico.Itens.Clear();
        File.Delete(novo);

        // som proprio
        byte[] wav = Som.GerarWav();
        Ok(wav.Length == 44 + 3528 * 2 && wav[0] == 'R' && wav[8] == 'W' && BitConverter.ToInt32(wav, 40) == 3528 * 2, "wav valido");
        int pico = 0;
        for (int i = 44; i + 1 < wav.Length; i += 2) pico = Math.Max(pico, Math.Abs((int)BitConverter.ToInt16(wav, i)));
        Console.WriteLine("pico do som: " + pico);
        Ok(pico > 8000, "som tem volume");
        Stopwatch ts = Stopwatch.StartNew();
        Ok(Som.Tocar(0), "Windows aceitou tocar o som");   // 0 = espera terminar
        Console.WriteLine("tocou por " + ts.ElapsedMilliseconds + " ms");

        // emoji colorido: tem pixels de cor?
        Stopwatch sw = Stopwatch.StartNew();
        Bitmap teste = EmojiCor.Criar("\U0001F600", 40, 27f);
        Console.WriteLine("primeiro emoji: " + sw.ElapsedMilliseconds + " ms");
        Ok(teste != null, "Direct2D desenhou");
        int coloridos = 0, transparentes = 0;
        for (int y = 0; y < 40; y++) for (int x = 0; x < 40; x++)
        {
            Color c = teste.GetPixel(x, y);
            if (c.A == 0) transparentes++;
            if (c.A > 200 && Math.Abs(c.R - c.B) > 60) coloridos++;
        }
        Console.WriteLine("coloridos=" + coloridos + " transparentes=" + transparentes);
        Ok(coloridos > 100, "emoji tem cor");
        Ok(transparentes > 100, "fundo transparente");
        teste.Dispose();

        sw.Restart();
        List<Emote> todos = Emotes.Todos;
        Console.WriteLine("carregar lista: " + sw.ElapsedMilliseconds + " ms, " + todos.Count + " emotes");
        sw.Restart();
        int feitos = 0, vazios = 0;
        foreach (Emote e in todos)
        {
            using (Bitmap b = EmojiCor.Criar(e.Simbolo, 40, 27f))
            {
                feitos++;
                bool tem = false;
                for (int k = 8; k < 32 && !tem; k += 3) if (b.GetPixel(k, k).A > 0 || b.GetPixel(k, 39 - k).A > 0 || b.GetPixel(20, k).A > 0) tem = true;
                if (!tem) { vazios++; if (vazios < 12) Console.WriteLine("vazio: " + e.Nome); }
            }
        }
        Console.WriteLine("todos: " + sw.ElapsedMilliseconds + " ms para " + feitos + " (" + (sw.ElapsedMilliseconds * 1000 / Math.Max(1, feitos)) + " us cada), vazios=" + vazios);

        // janela real, desenhada fora da tela
        Historico.Itens.Clear();
        foreach (string nome in new[] { "20261105_010000002.txt", "20261105_010000001.txt" })
        {
            string p2 = Path.Combine(saida, nome);
            File.WriteAllText(p2, "texto " + nome);
            Historico.Itens.Add(Item.DeTexto(p2, "texto " + nome));
        }
        Nucleo nuc = new Nucleo();
        sw.Restart();
        Janela j = new Janela(nuc, Janela.PgEmotes);
        Console.WriteLine("montar janela (emotes): " + sw.ElapsedMilliseconds + " ms");
        j.Location = new Point(-3000, -3000);
        j.Show();
        j.Location = new Point(-3000, -3000);
        Foto(j, saida, "emotes");
        sw.Restart(); j.MostrarPagina(Janela.PgConfig); Console.WriteLine("abrir config: " + sw.ElapsedMilliseconds + " ms");
        j.Height = 880;
        Foto(j, saida, "config");
        j.Height = 580;
        sw.Restart(); j.MostrarPagina(Janela.PgHistorico); Console.WriteLine("abrir historico: " + sw.ElapsedMilliseconds + " ms");
        Foto(j, saida, "historico");
        Console.WriteLine("estado: disposed="+j.IsDisposed+" visible="+j.Visible+" pagina="+j.Pagina+" itens="+Historico.Itens.Count);
        ListaCartoes lista = AcharLista(j);
        Console.WriteLine("lista="+(lista==null?"null":lista.Items.Count.ToString()));
        Ok(lista != null && lista.Items.Count >= 2, "lista do historico com itens");
        Console.WriteLine("rodape: " + AcharRodape(j));
        if (lista != null && lista.Items.Count >= 2)
        {
            int y1 = lista.GetItemRectangle(1).Top + 10;
            IntPtr pos = new IntPtr((y1 << 16) | 30);
            Nativo.SendMessage(lista.Handle, 0x0201, new IntPtr(1), pos);   // desce
            Nativo.SendMessage(lista.Handle, 0x0202, IntPtr.Zero, pos);     // sobe
            Application.DoEvents();
            Ok(lista.SelectedIndex == 1, "um clique seleciona");
            Ok(j.Resultado == null && !j.IsDisposed && j.Visible, "um clique nao fecha");
            Nativo.SendMessage(lista.Handle, 0x0203, new IntPtr(1), pos);   // segundo clique
            Nativo.SendMessage(lista.Handle, 0x0202, IntPtr.Zero, pos);
            Application.DoEvents();
            Ok(j.Resultado is Item && ReferenceEquals(j.Resultado, Historico.Itens[1]), "dois cliques escolhem o item clicado");
            Ok(j.IsDisposed || !j.Visible, "dois cliques fecham");
        }
        j.Dispose();
        nuc.Dispose();
        Console.WriteLine("janela fechou e soltou o Direct2D");

        // reabre depois de liberar
        Bitmap de_novo = EmojiCor.Criar("\u2764\uFE0F", 40, 27f);
        Ok(de_novo != null, "emoji depois de liberar");
        if (de_novo != null) de_novo.Dispose();
        EmojiCor.Liberar();

        Console.WriteLine(falhas == 0 ? "TUDO OK" : "FALHAS: " + falhas);
        Environment.Exit(falhas == 0 ? 0 : 1);
    }
}
#endif
