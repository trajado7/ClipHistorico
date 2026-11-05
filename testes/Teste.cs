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

    // ---- Editor de imagem ----
    static Bitmap Listras(int w, int h)
    {
        Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.Clear(Color.White);
            for (int x = 0; x < w; x += 6) g.FillRectangle(Brushes.Black, x, 0, 3, h);
        }
        return b;
    }

    static bool Iguais(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int y = 0; y < a.Height; y += 3)
            for (int x = 0; x < a.Width; x += 3)
                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) return false;
        return true;
    }

    static T Achar<T>(Control c) where T : Control
    {
        foreach (Control f in c.Controls)
        {
            T t = f as T;
            if (t != null) return t;
            t = Achar<T>(f);
            if (t != null) return t;
        }
        return null;
    }

    static Label AcharBotao(Control c, string texto)
    {
        foreach (Control f in c.Controls)
        {
            if (f is Label && f.Text == texto) return (Label)f;
            Label l = AcharBotao(f, texto);
            if (l != null) return l;
        }
        return null;
    }

    static IntPtr Pos(Point p) { return new IntPtr((p.Y << 16) | (p.X & 0xFFFF)); }

    static void Arrastar(Control c, Point de, Point ate)
    {
        Nativo.SendMessage(c.Handle, 0x0201, new IntPtr(1), Pos(de));
        Nativo.SendMessage(c.Handle, 0x0200, new IntPtr(1), Pos(new Point((de.X + ate.X) / 2, (de.Y + ate.Y) / 2)));
        Nativo.SendMessage(c.Handle, 0x0200, new IntPtr(1), Pos(ate));
        Nativo.SendMessage(c.Handle, 0x0202, IntPtr.Zero, Pos(ate));
        Application.DoEvents();
    }

    static void FotoForm(Form f, string caminho)
    {
        using (Bitmap foto = new Bitmap(f.Width, f.Height))
        {
            f.DrawToBitmap(foto, new Rectangle(0, 0, f.Width, f.Height));
            foto.Save(caminho, ImageFormat.Png);
        }
    }

    static void TestarEditor(string saida)
    {
        // Pixels e desfazer
        using (Bitmap orig = Listras(300, 200))
        using (Quadro q = new Quadro(orig))
        {
            Ok(!q.Alterado && !q.PodeDesfazer, "quadro novo sem alteracao");

            q.ComecarTraco(Ferramenta.Lapis, Color.Red, 8, new PointF(30, 30));
            q.MoverTraco(new PointF(120, 90));
            q.TerminarTraco();
            Ok(q.Imagem.GetPixel(75, 60).R > 200 && q.Imagem.GetPixel(75, 60).G < 60, "lapis pinta");
            Ok(q.Alterado && q.PodeDesfazer, "lapis marca alteracao");

            q.Desfazer();
            Ok(Iguais(q.Imagem, orig), "desfazer volta exatamente");
            Ok(!q.Alterado, "desfazer tudo limpa a alteracao");
            q.Refazer();
            Ok(q.Imagem.GetPixel(75, 60).R > 200 && q.Alterado, "refazer repete");
            q.Desfazer();

            // clique simples
            q.ComecarTraco(Ferramenta.Lapis, Color.Lime, 10, new PointF(200, 100));
            q.TerminarTraco();
            Ok(q.Imagem.GetPixel(200, 100).G > 200, "clique sem arrastar marca");
            q.Desfazer();

            // borracha
            q.ComecarTraco(Ferramenta.Borracha, Color.Red, 20, new PointF(50, 50));
            q.MoverTraco(new PointF(100, 50));
            q.TerminarTraco();
            Ok(q.Imagem.GetPixel(75, 50).A == 0, "borracha deixa transparente");
            Ok(q.Imagem.GetPixel(75, 120).A == 255, "borracha nao passa do traco");
            q.Desfazer();
            Ok(Iguais(q.Imagem, orig), "desfazer borracha");

            // borrar: as listras nao podem mais ser lidas
            q.ComecarTraco(Ferramenta.BorrarArea, Color.Red, 8, new PointF(20, 20));
            q.MoverTraco(new PointF(280, 180));
            q.TerminarTraco();
            int menor = 255, maior = 0;
            for (int x = 100; x < 160; x++) { int v = q.Imagem.GetPixel(x, 100).R; menor = Math.Min(menor, v); maior = Math.Max(maior, v); }
            Ok(maior - menor < 60, "borrar some com o padrao (variacao " + (maior - menor) + ")");
            string borrada = Path.Combine(saida, "borrada.png");
            q.Imagem.Save(borrada, ImageFormat.Png);
            using (Bitmap salva = new Bitmap(borrada))
            {
                int s1 = 255, s2 = 0;
                for (int x = 100; x < 160; x++) { int v = salva.GetPixel(x, 100).R; s1 = Math.Min(s1, v); s2 = Math.Max(s2, v); }
                Ok(s2 - s1 < 60, "o arquivo salvo nao guarda o original");
            }
            q.Desfazer();
            Ok(Iguais(q.Imagem, orig), "desfazer borrar");

            // borrar com pincel
            q.ComecarTraco(Ferramenta.Borrar, Color.Red, 40, new PointF(60, 100));
            q.MoverTraco(new PointF(240, 100));
            q.TerminarTraco();
            Ok(!Iguais(q.Imagem, orig), "pincel de borrar muda a imagem");
            q.Desfazer();

            // cancelar e traco vazio
            q.ComecarTraco(Ferramenta.Seta, Color.Red, 4, new PointF(30, 30));
            q.MoverTraco(new PointF(200, 150));
            q.CancelarTraco();
            Ok(Iguais(q.Imagem, orig), "cancelar nao deixa rastro");
            q.ComecarTraco(Ferramenta.Retangulo, Color.Red, 4, new PointF(30, 30));
            q.TerminarTraco();
            Ok(!q.PodeDesfazer, "clique com retangulo nao cria passo");

            // formas redesenhadas nao deixam rastro do caminho
            q.ComecarTraco(Ferramenta.Retangulo, Color.Red, 2, new PointF(10, 10));
            q.MoverTraco(new PointF(280, 180));
            q.MoverTraco(new PointF(60, 60));
            q.TerminarTraco();
            Ok(q.Imagem.GetPixel(280, 100).ToArgb() == orig.GetPixel(280, 100).ToArgb(), "forma nao deixa rastro");
            q.Desfazer();

            // recorte
            Ok(q.Recortar(new Rectangle(10, 20, 100, 50)), "recorta");
            Ok(q.Largura == 100 && q.Altura == 50, "tamanho do recorte");
            q.ComecarTraco(Ferramenta.Lapis, Color.Red, 6, new PointF(20, 20));
            q.MoverTraco(new PointF(80, 20));
            q.TerminarTraco();
            q.Desfazer();
            q.Desfazer();
            Ok(q.Largura == 300 && Iguais(q.Imagem, orig), "desfazer recorte devolve a imagem");
            q.Refazer();
            Ok(q.Largura == 100, "refazer recorte");
            q.Desfazer();
            Ok(!q.Recortar(new Rectangle(500, 500, 10, 10)), "recorte fora da imagem recusado");

            // imagem por cima e texto
            using (Bitmap sobre = new Bitmap(40, 40))
            {
                using (Graphics g = Graphics.FromImage(sobre)) g.Clear(Color.Blue);
                q.Estampar(sobre, new RectangleF(100, 100, 80, 80));
            }
            Ok(q.Imagem.GetPixel(140, 140).B > 200 && q.Imagem.GetPixel(140, 140).R < 60, "imagem por cima");
            q.Desfazer();
            using (Font f = new Font("Segoe UI Semibold", 40, GraphicsUnit.Pixel)) q.Escrever("Oi", f, Color.Red, new PointF(20, 20));
            Ok(!Iguais(q.Imagem, orig), "texto escreve");
            q.Desfazer();
            Ok(Iguais(q.Imagem, orig), "desfazer texto");

            // limite de passos
            for (int i = 0; i < 90; i++)
            {
                q.ComecarTraco(Ferramenta.Lapis, Color.Red, 4, new PointF(10 + i, 10));
                q.TerminarTraco();
            }
            int n = 0;
            while (q.Desfazer()) n++;
            Ok(n == 60, "guarda 60 passos (" + n + ")");
        }

        // Copiar: confere o que chega na area de transferencia
        string guardado = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        using (Bitmap b = Listras(50, 30)) Ok(App.CopiarEdicao(b, false), "copiar edicao");
        Ok(Clipboard.ContainsImage(), "imagem na area de transferencia");
        using (Image lida = Clipboard.GetImage()) Ok(lida != null && lida.Width == 50, "imagem copiada com o tamanho certo");
        if (guardado != null) Clipboard.SetText(guardado); else Clipboard.Clear();

        // Gravar nos tres formatos
        foreach (string ext in new[] { ".png", ".jpg", ".bmp" })
        {
            string arq = Path.Combine(saida, "gravado" + ext);
            using (Bitmap b = Listras(40, 40)) Historico.Gravar(b, arq);
            using (Bitmap l = new Bitmap(arq)) Ok(l.Width == 40, "gravar " + ext);
            File.Delete(arq);
        }

        // Janela de verdade, fora da area visivel, com cliques simulados
        EditorImagem ed = new EditorImagem(Listras(1600, 900));
        ed.Location = new Point(-6000, 0);
        ed.Show();
        Application.DoEvents();
        ed.Ajustar();
        Application.DoEvents();
        FotoForm(ed, Path.Combine(saida, "editor.png"));
        Console.WriteLine("janela do editor: " + ed.Width + "x" + ed.Height);

        foreach (string nome in new[] { "Copiar", "Salvar como...", "Fechar", "Mover", "Recortar", "Imagem por cima" })
        {
            Label l = AcharBotao(ed, nome);
            Ok(l != null && l.Right <= ed.Width && l.Left >= 0, "botao na tela: " + nome + (l == null ? "" : " x=" + l.Left));
        }
        Ok(AcharBotao(ed, "Imagem por cima").Right < AcharBotao(ed, "Copiar").Left, "botoes nao se sobrepoem");

        TelaEditor tela = Achar<TelaEditor>(ed);
        Ok(tela != null && tela.ClientSize.Width > 800 && tela.ClientSize.Height > 400, "tela de desenho ocupa o espaco");

        Quadro q2 = null;
        foreach (System.Reflection.FieldInfo fi in typeof(EditorImagem).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
            if (fi.FieldType == typeof(Quadro)) q2 = (Quadro)fi.GetValue(ed);

        Arrastar(tela, new Point(300, 200), new Point(600, 300));
        FotoForm(ed, Path.Combine(saida, "editor-rabisco.png"));
        Ok(q2.Alterado, "arrastar o mouse rabisca");
        ed.Desfazer();
        Ok(!q2.Alterado, "desfazer na janela");

        ed.TrocarFerramenta(Ferramenta.Recortar);
        Arrastar(tela, new Point(300, 200), new Point(800, 500));
        Ok(q2.Largura < 1600, "recortar na janela (" + q2.Largura + "x" + q2.Altura + ")");
        ed.Desfazer();
        Ok(q2.Largura == 1600, "desfazer recorte na janela");

        Bitmap solta = new Bitmap(200, 200);
        using (Graphics g = Graphics.FromImage(solta)) g.Clear(Color.Blue);
        ed.ColocarImagem(solta);
        Application.DoEvents();
        FotoForm(ed, Path.Combine(saida, "editor-imagem.png"));
        Ok(!q2.Alterado, "imagem por cima fica solta ate fixar");
        ed.TrocarFerramenta(Ferramenta.Lapis);
        Ok(q2.Alterado, "trocar de ferramenta fixa a imagem");
        ed.Desfazer();

        ed.Dispose();
    }

    // ---- Rolagem do editor e recorte de tela ----
    static float Campo(EditorImagem ed, string nome)
    {
        return (float)typeof(EditorImagem).GetField(nome, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(ed);
    }

    static void TestarRolagemERecorte(string saida)
    {
        EditorImagem ed = new EditorImagem(Listras(3000, 2000));
        ed.Location = new Point(-6000, 0);
        ed.Show();
        Application.DoEvents();
        ed.Ajustar();
        Point meio = new Point(500, 400);

        float z0 = Campo(ed, "zoom"), x0 = Campo(ed, "ox"), y0 = Campo(ed, "oy");
        ed.Rolar(120, Keys.Shift, meio);
        Ok(Campo(ed, "ox") == x0 && Campo(ed, "oy") == y0, "imagem que cabe nao anda");

        ed.Rolar(120, Keys.Control, meio);
        Ok(Campo(ed, "zoom") > z0, "ctrl + scroll aproxima");
        float z1 = Campo(ed, "zoom");
        ed.Rolar(-120, Keys.Control, meio);
        Ok(Campo(ed, "zoom") < z1, "ctrl + scroll afasta");

        for (int i = 0; i < 8; i++) ed.Rolar(120, Keys.Control, meio);
        float z2 = Campo(ed, "zoom");
        PointF antes = new PointF(Campo(ed, "ox"), Campo(ed, "oy"));

        ed.Rolar(-120, Keys.Shift, meio);
        Ok(Campo(ed, "ox") == antes.X - 60 && Campo(ed, "oy") == antes.Y, "shift + scroll para baixo vai para a direita");
        ed.Rolar(120, Keys.Shift, meio);
        Ok(Campo(ed, "ox") == antes.X, "shift + scroll para cima vai para a esquerda");
        ed.Rolar(-120, Keys.None, meio);
        Ok(Campo(ed, "oy") == antes.Y - 60 && Campo(ed, "ox") == antes.X, "scroll sozinho anda para baixo");
        Ok(Campo(ed, "zoom") == z2, "andar nao muda o zoom");

        for (int i = 0; i < 400; i++) ed.Rolar(-120, Keys.Shift, meio);
        TelaEditor tela = Achar<TelaEditor>(ed);
        Ok(Math.Abs(Campo(ed, "ox") + 3000 * z2 - tela.ClientSize.Width + 16) < 1, "para na beirada direita");

        // ponto embaixo do mouse fica no lugar ao aproximar
        ed.Ajustar();
        PointF img1 = new PointF((meio.X - Campo(ed, "ox")) / Campo(ed, "zoom"), (meio.Y - Campo(ed, "oy")) / Campo(ed, "zoom"));
        ed.Rolar(120, Keys.Control, meio);
        PointF img2 = new PointF((meio.X - Campo(ed, "ox")) / Campo(ed, "zoom"), (meio.Y - Campo(ed, "oy")) / Campo(ed, "zoom"));
        Ok(Math.Abs(img1.X - img2.X) < 2 && Math.Abs(img1.Y - img2.Y) < 2, "aproxima no ponto do mouse");

        // imagem solta nao e fixada pela rolagem
        Bitmap solta = new Bitmap(100, 100);
        ed.ColocarImagem(solta);
        ed.Rolar(120, Keys.Control, meio);
        ed.Rolar(-120, Keys.None, meio);
        Quadro q = null;
        foreach (System.Reflection.FieldInfo fi in typeof(EditorImagem).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
            if (fi.FieldType == typeof(Quadro)) q = (Quadro)fi.GetValue(ed);
        Ok(!q.Alterado, "rolar nao fixa a imagem solta");
        ed.Desfazer();
        ed.Dispose();

        // Foto da tela inteira
        Rectangle area;
        using (Bitmap tudo = Captura.TirarTudo(out area))
        {
            Ok(tudo.Width == area.Width && tudo.Height == area.Height && tudo.Width > 100, "foto da tela " + tudo.Width + "x" + tudo.Height);
        }

        // Recorte, fora da area visivel, com uma foto conhecida
        Func<Bitmap> fotoTeste = () =>
        {
            Bitmap b = new Bitmap(800, 600, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.White);
                g.FillRectangle(Brushes.Red, 100, 100, 200, 150);
            }
            return b;
        };
        Rectangle fora = new Rectangle(-6000, 0, 800, 600);

        TelaRecorte r = new TelaRecorte(fotoTeste(), fora);
        r.Mostrar();
        Application.DoEvents();
        Ok(r.ClientSize.Width == 800 && r.ClientSize.Height == 600, "janela do recorte do tamanho da foto (" + r.ClientSize.Width + "x" + r.ClientSize.Height + ")");
        FotoForm(r, Path.Combine(saida, "recorte-vazio.png"));

        Nativo.SendMessage(r.Handle, 0x0201, new IntPtr(1), Pos(new Point(10, 10)));
        Nativo.SendMessage(r.Handle, 0x0202, IntPtr.Zero, Pos(new Point(10, 10)));
        Application.DoEvents();
        Ok(r.Resultado == null && !r.IsDisposed && r.Visible, "clique sem arrastar nao recorta");

        Nativo.SendMessage(r.Handle, 0x0201, new IntPtr(1), Pos(new Point(100, 100)));
        Nativo.SendMessage(r.Handle, 0x0200, new IntPtr(1), Pos(new Point(200, 150)));
        Nativo.SendMessage(r.Handle, 0x0200, new IntPtr(1), Pos(new Point(300, 250)));
        Application.DoEvents();
        FotoForm(r, Path.Combine(saida, "recorte-selecao.png"));
        Nativo.SendMessage(r.Handle, 0x0202, IntPtr.Zero, Pos(new Point(300, 250)));
        Application.DoEvents();
        Ok(r.Resultado != null && r.Resultado.Width == 200 && r.Resultado.Height == 150, "tamanho recortado");
        if (r.Resultado != null)
        {
            Color c1 = r.Resultado.GetPixel(0, 0), c2 = r.Resultado.GetPixel(199, 149);
            Ok(c1.R == 255 && c1.G == 0 && c2.R == 255 && c2.G == 0, "recorte pega os pixels certos, sem escurecer");
            r.Resultado.Dispose();
        }

        // arrastar ao contrario e passar da beirada
        r = new TelaRecorte(fotoTeste(), fora);
        r.Mostrar();
        Application.DoEvents();
        Nativo.SendMessage(r.Handle, 0x0201, new IntPtr(1), Pos(new Point(700, 500)));
        Nativo.SendMessage(r.Handle, 0x0200, new IntPtr(1), Pos(new Point(-50, -50)));
        Nativo.SendMessage(r.Handle, 0x0202, IntPtr.Zero, Pos(new Point(-50, -50)));
        Application.DoEvents();
        Ok(r.Resultado != null && r.Resultado.Width == 700 && r.Resultado.Height == 500, "arrastar ao contrario ate a beirada");
        if (r.Resultado != null) r.Resultado.Dispose();

        // cancelar
        r = new TelaRecorte(fotoTeste(), fora);
        r.Mostrar();
        Application.DoEvents();
        Nativo.SendMessage(r.Handle, 0x0204, new IntPtr(2), Pos(new Point(50, 50)));
        Application.DoEvents();
        Ok(r.Resultado == null && (r.IsDisposed || !r.Visible), "botao direito cancela");
        r.Dispose();

        Ok(Config.Atalhos[Config.RecortarTela].Mods == (Atalho.Win | Atalho.Shift) && Config.Atalhos[Config.RecortarTela].Vk == 0x53, "atalho padrao Win + Shift + S");
        Ok(Config.Atalhos.Length == Config.NomesAcoes.Length, "todo atalho tem nome");
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

        TestarEditor(saida);
        TestarRolagemERecorte(saida);

        Console.WriteLine(falhas == 0 ? "TUDO OK" : "FALHAS: " + falhas);
        Environment.Exit(falhas == 0 ? 0 : 1);
    }
}
#endif
