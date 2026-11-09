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

    // ---- Salvos com imagem ----
    static object Chamar(object o, string metodo, params object[] args)
    {
        return o.GetType().GetMethod(metodo, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).Invoke(o, args);
    }

    static void TestarSalvos(string saida, Nucleo nuc)
    {
        string pasta = Path.Combine(saida, "salvos-teste");
        if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
        Salvos.PastaTeste = pasta;
        Salvos.Esquecer();

        Salvo so = Salvos.Adicionar("So texto, como sempre foi");
        Salvo ambos;
        using (Bitmap b = Listras(400, 300)) ambos = Salvos.Adicionar("Oferta de hoje\nChame no privado", b);
        Salvo soImg;
        using (Bitmap b = Listras(200, 200)) soImg = Salvos.Adicionar("", b);

        Ok(!so.TemImagem && ambos.TemImagem && ambos.TemTexto && soImg.TemImagem && !soImg.TemTexto, "tipos de salvo");
        Ok(File.Exists(Path.ChangeExtension(ambos.Caminho, ".png")), "imagem fica ao lado do texto");
        Ok(soImg.Titulo == "Imagem salva", "titulo do salvo so com imagem");
        Ok(ambos.Foto.Mini != null, "miniatura do salvo");

        // sobrevive a reabrir o programa
        Salvos.Esquecer();
        Ok(Salvos.Itens.Count == 3, "salvos voltam do disco");
        Salvo volta = null;
        foreach (Salvo s in Salvos.Itens) if (s.Texto.StartsWith("Oferta")) volta = s;
        Ok(volta != null && volta.TemImagem && volta.Foto.Info == "400 x 300", "imagem volta com o salvo");
        ambos = volta;

        // a imagem do salvo nao entra no historico nem vence
        Ok(!Historico.EhDoHistorico(Path.Combine(Config.Pasta, "x.png")) || !Path.GetDirectoryName(ambos.Caminho).Equals(Config.Pasta), "salvo fora da pasta do historico");

        // Tela de verdade
        Janela j = new Janela(nuc, Janela.PgSalvos);
        j.Location = new Point(-6000, 0);
        j.Show();
        Application.DoEvents();
        ListaCartoes lista = AcharLista(j);
        Ok(lista != null && lista.Items.Count == 3, "lista mostra os salvos");
        int ind = -1;
        for (int i = 0; i < lista.Items.Count; i++) if (((List<Salvo>)CampoJ(j, "visSalvos"))[i] == ambos) ind = i;
        lista.SelectedIndex = ind;
        Application.DoEvents();
        Foto(j, saida, "salvos");
        PictureBox pv = (PictureBox)CampoJ(j, "previaSalvo");
        Ok(pv.Visible && pv.Image != null, "previa mostra a imagem");

        // editar: cancelar nao muda nada
        Chamar(j, "ComecarEdicao", ambos);
        Chamar(j, "TirarImagemSalvo");
        Ok(!pv.Visible, "tirar imagem some da previa");
        Chamar(j, "CancelarEdicao");
        Ok(ambos.TemImagem && File.Exists(ambos.Foto.Caminho), "cancelar mantem a imagem");
        Ok(pv.Visible, "cancelar volta a previa");

        Chamar(j, "ComecarEdicao", ambos);
        Chamar(j, "AnexarImagem", Listras(120, 90));
        Application.DoEvents();
        Foto(j, saida, "salvos-editando");
        Chamar(j, "CancelarEdicao");
        Ok(ambos.Foto.Info == "400 x 300", "cancelar nao troca a imagem");

        // editar: trocar de verdade
        Chamar(j, "ComecarEdicao", ambos);
        Chamar(j, "AnexarImagem", Listras(120, 90));
        Chamar(j, "SalvarEdicao");
        Ok(ambos.Foto.Info == "120 x 90", "salvar troca a imagem");

        // novo salvo com imagem pela tela
        Chamar(j, "ComecarEdicao", new object[] { null });
        ((TextBoxBase)CampoJ(j, "editor")).Text = "Salvo novo pela tela";
        Chamar(j, "AnexarImagem", Listras(64, 64));
        Chamar(j, "SalvarEdicao");
        Salvo novoTela = Salvos.Itens[0];
        Ok(novoTela.Texto == "Salvo novo pela tela" && novoTela.TemImagem, "novo salvo com imagem");

        // tirar a imagem e tirar o texto de um so com imagem
        Chamar(j, "ComecarEdicao", novoTela);
        Chamar(j, "TirarImagemSalvo");
        Chamar(j, "SalvarEdicao");
        Ok(!novoTela.TemImagem && !File.Exists(Path.ChangeExtension(novoTela.Caminho, ".png")), "tirar imagem apaga o arquivo");

        // Envio escolhe o tipo certo
        object e1 = typeof(Janela).GetMethod("Envio", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { ambos });
        object e2 = typeof(Janela).GetMethod("Envio", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { so });
        object e3 = typeof(Janela).GetMethod("Envio", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { soImg });
        Ok(e1 is EnvioSalvo && ((EnvioSalvo)e1).ComTexto, "salvo completo envia os dois");
        Ok(e2 is string, "salvo so de texto continua igual");
        Ok(e3 is EnvioSalvo && !((EnvioSalvo)e3).ComTexto, "salvo so de imagem envia a imagem");
        j.Dispose();

        // apagar leva a imagem junto
        string pngAmbos = ambos.Foto.Caminho;
        Salvos.Remover(ambos);
        Ok(!File.Exists(pngAmbos) && !File.Exists(ambos.Caminho), "apagar remove texto e imagem");

        // Colagem real em duas etapas, numa janela que aceita imagem e texto
        string guardado = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        Form alvo = new Form();
        alvo.Text = "alvo";
        alvo.Size = new Size(500, 300);
        alvo.StartPosition = FormStartPosition.Manual;
        alvo.Location = new Point(100, 100);
        RichTextBox rt = new RichTextBox();
        rt.Dock = DockStyle.Fill;
        alvo.Controls.Add(rt);
        alvo.Show();
        alvo.Activate();
        Nativo.SetForegroundWindow(alvo.Handle);
        rt.Focus();
        Application.DoEvents();
        bool emFoco = Nativo.GetForegroundWindow() == alvo.Handle;
        Console.WriteLine("janela alvo em foco: " + emFoco);

        using (Bitmap b = Listras(40, 40)) App.PorImagem(b);
        SendKeys.SendWait("^v");
        Application.DoEvents();
        bool terminou = false;
        App.ColarLegenda("LEGENDA-DE-TESTE", alvo.Handle, 200, delegate { terminou = true; });
        Stopwatch espera = Stopwatch.StartNew();
        while (!terminou && espera.ElapsedMilliseconds < 3000) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        Application.DoEvents();
        Ok(terminou, "legenda foi colada no tempo");
        if (emFoco)
        {
            Ok(rt.Rtf.Contains("\\pict") || rt.Rtf.Contains("\\object"), "imagem entrou na janela");
            Ok(rt.Text.Contains("LEGENDA-DE-TESTE"), "texto entrou depois da imagem");
            Ok(rt.Text.IndexOf("LEGENDA") == rt.Text.LastIndexOf("LEGENDA"), "texto entrou uma vez so");
        }
        Ok(Clipboard.ContainsText() && Clipboard.GetText() == "LEGENDA-DE-TESTE", "texto fica na area de transferencia");

        // mudou de janela: nao cola
        rt.Clear();
        terminou = false;
        App.ColarLegenda("NAO-PODE-COLAR", new IntPtr(1), 50, delegate { terminou = true; });
        espera.Restart();
        while (!terminou && espera.ElapsedMilliseconds < 3000) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        Application.DoEvents();
        Ok(!rt.Text.Contains("NAO-PODE-COLAR"), "outra janela na frente: nao cola");
        Ok(Clipboard.GetText() == "NAO-PODE-COLAR", "mas o texto fica copiado");

        alvo.Dispose();
        if (guardado != null) Clipboard.SetText(guardado); else Clipboard.Clear();
        Salvos.PastaTeste = null;
        Salvos.Esquecer();
    }

    static object CampoJ(Janela j, string nome)
    {
        return typeof(Janela).GetField(nome, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(j);
    }

    // ---- Clipes e galeria ----
    static bool Esperar(Func<bool> condicao, int ms)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!condicao() && sw.ElapsedMilliseconds < ms) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
        return condicao();
    }

    static void Pedaco(string pasta, string nome, DateTime quando)
    {
        string f = Path.Combine(pasta, nome);
        File.WriteAllText(f, nome);
        File.SetLastWriteTimeUtc(f, quando);
    }

    static string Sondar(string arquivo)
    {
        string s;
        Ffmpeg.Rodar("-hide_banner -i " + Ffmpeg.Aspas(arquivo), null, 15000, out s);
        return s;
    }

    static void TestarClipes(string saida)
    {
        // qualidade e linha de comando
        ConfigClipe.UsarQualidade(1);
        Ok(ConfigClipe.Altura == 1080 && ConfigClipe.Fps == 60 && ConfigClipe.Mbps == 20 && ConfigClipe.Qualidade() == 1, "qualidade media");
        ConfigClipe.Mbps = 21;
        Ok(ConfigClipe.Qualidade() == 3, "mexeu nos bits: personalizada");
        ConfigClipe.UsarQualidade(2);

        Replay.Opcoes o = new Replay.Opcoes();
        o.Fps = 60; o.Mbps = 50; o.Segundos = 30; o.Monitor = 1; o.Codificador = "h264_nvenc"; o.MostrarCursor = true;
        string a = Replay.MontarArgs(o, null, null, 3, 0);
        Console.WriteLine(a);
        Ok(a.Contains("ddagrab=output_idx=1:framerate=60:draw_mouse=1") && !a.Contains("-vf") && !a.Contains("-c:a"), "nativa: tudo na placa, sem filtro e sem som");
        Ok(a.Contains("-segment_wrap 38") && a.Contains("-segment_list_size 38") && a.Contains("-b:v 50000k -maxrate 75000k"), "anel e taxa de bits");
        Ok(a.Contains("-segment_list lista_s00003.csv") && a.EndsWith("s00003_%04d.ts") && !a.Contains("output_ts_offset"), "nomes da sessao, sem deslocamento na primeira");
        a = Replay.MontarArgs(o, null, null, 4, 12.5);
        Ok(a.Contains("-output_ts_offset 12.500 ") && a.EndsWith("s00004_%04d.ts"), "sessao seguinte continua o relogio (com ponto, nao virgula)");
        o.Altura = 720;
        a = Replay.MontarArgs(o, "-f f32le -i A", "-f f32le -i B", 1, 0);
        Ok(a.Contains("hwdownload") && a.Contains("min(720,ih)") && a.Contains("amix=inputs=2") && a.Contains("-map \"[som]\"") && a.Contains("-c:a aac"), "720p com som do PC e microfone");
        a = Replay.MontarArgs(o, "-f f32le -i A", null, 1, 0);
        Ok(a.Contains("-map 1:a") && !a.Contains("amix"), "so o som do PC");
        o.Altura = 0; o.Codificador = "libx264";
        Ok(Replay.Filtro(o).EndsWith("format=yuv420p"), "x264 traz a imagem para o processador");
        Ok(Vigia.PerdeuATela("[Parsed_ddagrab_0 @ 0000021a4a234500] AcquireNextFrame failed: 887a0026") &&
           !Vigia.PerdeuATela("frame=120"), "reconhece a perda da tela (BlueStacks)");

        // escolha dos pedacos do anel
        string anel = Path.Combine(saida, "anel");
        if (Directory.Exists(anel)) Directory.Delete(anel, true);
        Directory.CreateDirectory(anel);
        DateTime t0 = DateTime.UtcNow.AddSeconds(-20);
        Pedaco(anel, "s00001_0000.ts", t0.AddSeconds(-30));   // sobra da volta anterior do anel
        for (int i = 5; i <= 9; i++) Pedaco(anel, "s00001_" + i.ToString("0000") + ".ts", t0.AddSeconds(i));
        string l1 = Path.Combine(anel, "lista_s00001.csv");
        File.WriteAllText(l1, "s00001_0005.ts,5.0,6.0\ns00001_0006.ts,6.0,7.0\ns00001_0007.ts,7.0,8.0\ns00001_0008.ts,8.0,9.0\n");
        List<string> p = Replay.EscolherPedacos(anel, 3, 10);
        Ok(string.Join("|", p.ToArray()) == "s00001_0006.ts|s00001_0007.ts|s00001_0008.ts|s00001_0009.ts", "ultimos 3 prontos + o que esta gravando: " + string.Join("|", p.ToArray()));
        File.AppendAllText(l1, "s00001_0009.ts,9.0,10.0\n");
        p = Replay.EscolherPedacos(anel, 3, 10);
        Ok(string.Join("|", p.ToArray()) == "s00001_0007.ts|s00001_0008.ts|s00001_0009.ts", "nao emenda a sobra velha do anel: " + string.Join("|", p.ToArray()));

        // a captura caiu e voltou numa sessao nova: o clipe passa pelas duas
        Pedaco(anel, "s00002_0000.ts", t0.AddSeconds(11));
        File.WriteAllText(Path.Combine(anel, "lista_s00002.csv"), "");
        p = Replay.EscolherPedacos(anel, 3, 10);
        Ok(string.Join("|", p.ToArray()) == "s00001_0007.ts|s00001_0008.ts|s00001_0009.ts|s00002_0000.ts", "sessao nova acabou de comecar: " + string.Join("|", p.ToArray()));
        Pedaco(anel, "s00002_0001.ts", t0.AddSeconds(12));
        File.WriteAllText(Path.Combine(anel, "lista_s00002.csv"), "s00002_0000.ts,0.0,1.0\n");
        p = Replay.EscolherPedacos(anel, 3, 10);
        Ok(string.Join("|", p.ToArray()) == "s00001_0008.ts|s00001_0009.ts|s00002_0000.ts|s00002_0001.ts", "emenda as duas sessoes: " + string.Join("|", p.ToArray()));
        Pedaco(anel, "s00000_0003.ts", t0.AddSeconds(-60));   // sessao bem velha
        File.WriteAllText(Path.Combine(anel, "lista_s00000.csv"), "s00000_0003.ts,3.0,4.0\n");
        p = Replay.EscolherPedacos(anel, 10, 10);
        Ok(!p.Contains("s00000_0003.ts"), "pedaco velho demais nao entra no clipe: " + string.Join("|", p.ToArray()));
        Replay.LimparAnel(anel, 3, true);
        Ok(!File.Exists(Path.Combine(anel, "s00000_0003.ts")) && !File.Exists(Path.Combine(anel, "lista_s00000.csv")) &&
           File.Exists(Path.Combine(anel, "s00002_0001.ts")), "limpeza tira so a sessao velha");
        foreach (string f in Directory.GetFiles(anel, "lista_*.csv")) File.Delete(f);
        Ok(Replay.EscolherPedacos(anel, 3, 10).Count == 0, "sem lista, sem pedacos");

        Ok(Miniaturas.LerDuracao("  Duration: 00:01:05.60, start: 0") == "1:06", "duracao 1:06");
        Ok(Miniaturas.LerDuracao("Duration: 01:02:03.00,") == "1:02:03", "duracao com hora");
        Ok(GradeMidias.NomeDia(DateTime.Now) == "Hoje" && GradeMidias.NomeDia(DateTime.Now.AddDays(-1)) == "Ontem", "hoje e ontem");
        Console.WriteLine("dia antigo: " + GradeMidias.NomeDia(new DateTime(2026, 11, 3)));
        Ok(Replay.Tamanho(5 * 1024 * 1024) == "5,0 MB" || Replay.Tamanho(5 * 1024 * 1024) == "5.0 MB", "tamanho em MB: " + Replay.Tamanho(5 * 1024 * 1024));

        string clipes = Path.Combine(saida, "clipes");
        if (Directory.Exists(clipes)) Directory.Delete(clipes, true);
        Directory.CreateDirectory(clipes);
        TestarGravacao(clipes);
        TestarTravamento();
        TestarGravacaoNormal(clipes);
        TestarGaleria(saida, clipes);
    }

    // Grava a tela de verdade, salva um clipe e confere o arquivo
    static void TestarGravacao(string clipes)
    {
        if (Ffmpeg.Caminho == null) { Ok(false, "ffmpeg encontrado"); return; }
        Console.WriteLine("ffmpeg: " + Ffmpeg.Caminho);
        ConfigClipe.Pasta = clipes;
        ConfigClipe.Segundos = 15;
        ConfigClipe.UsarQualidade(2);
        ConfigClipe.Codec = "h264";
        ConfigClipe.SomPc = true;
        ConfigClipe.Microfone = false;
        ConfigClipe.Ligado = true;

        WindowsFormsSynchronizationContext ctx = new WindowsFormsSynchronizationContext();
        System.Threading.SynchronizationContext.SetSynchronizationContext(ctx);
        Stopwatch sw = Stopwatch.StartNew();
        Replay.Preparar(ctx);
        Esperar(delegate { return Replay.Gravando || (!Replay.Iniciando && Replay.Problema != null); }, 30000);
        Console.WriteLine("iniciou em " + sw.ElapsedMilliseconds + " ms: " + Replay.StatusLongo);
        Ok(Replay.Gravando, "replay gravando");
        if (!Replay.Gravando) return;

        // uso do processador pelo ffmpeg enquanto grava
        Process[] ps = Process.GetProcessesByName("ffmpeg");
        TimeSpan antes = TimeSpan.Zero;
        foreach (Process x in ps) try { antes += x.TotalProcessorTime; } catch { }
        Esperar(delegate { return false; }, 7000);
        TimeSpan depois = TimeSpan.Zero;
        foreach (Process x in ps) try { x.Refresh(); depois += x.TotalProcessorTime; } catch { }
        Console.WriteLine("ffmpeg usou " + ((depois - antes).TotalMilliseconds / 7000.0 * 100 / Environment.ProcessorCount).ToString("0.0") +
                          "% do processador (" + ((depois - antes).TotalMilliseconds / 70.0).ToString("0") + "% de um nucleo)");

        sw.Restart();
        Replay.SalvarClipe();
        Esperar(delegate { return !Replay.Salvando; }, 30000);
        Console.WriteLine("salvou em " + sw.ElapsedMilliseconds + " ms");
        string[] feitos = Directory.GetFiles(clipes, "*.mp4");
        Ok(feitos.Length == 1, "um clipe salvo");
        if (feitos.Length == 1)
        {
            string s = Sondar(feitos[0]);
            Console.WriteLine(s);
            string d = Miniaturas.LerDuracao(s);
            Ok(s.Contains("Video: h264") && s.Contains("Audio: aac"), "clipe tem video h264 e som aac");
            Ok(d == "0:06" || d == "0:07" || d == "0:08" || d == "0:09", "clipe curto tem o tempo gravado ate agora: " + d);
            string erros;
            int cod = Ffmpeg.Rodar("-hide_banner -v error -i " + Ffmpeg.Aspas(feitos[0]) + " -f null -", null, 60000, out erros);
            Ok(cod == 0 && string.IsNullOrEmpty(erros.Trim()), "fim do clipe sem quadro cortado: " + erros);
        }

        // passou da duracao: o clipe fica com 15 s (+ o pedaco em andamento)
        Esperar(delegate { return false; }, 12000);
        Replay.SalvarClipe();
        Esperar(delegate { return !Replay.Salvando; }, 30000);
        feitos = Directory.GetFiles(clipes, "*.mp4");
        Ok(feitos.Length == 2, "segundo clipe salvo");
        if (feitos.Length == 2)
        {
            Array.Sort(feitos);
            string d = Miniaturas.LerDuracao(Sondar(feitos[1]));
            // 15 pedacos; se o Windows tirar a tela no meio (Alt+Tab num jogo), o
            // clipe passa pelo buraco e fica um pouco mais longo que 15 s
            Ok(d == "0:15" || d == "0:16" || d == "0:17" || d == "0:18", "clipe longo limitado a 15 s: " + d);
        }
        string[] anel = Directory.GetFiles(ConfigClipe.PastaAnel, "s*_*.ts");
        Ok(anel.Length <= 15 + Replay.Folga, "anel nao passa do limite: " + anel.Length + " pedacos");

        Process p = null;
        foreach (Process x in Process.GetProcessesByName("ffmpeg")) p = x;
        Replay.Parar();
        Ok(p == null || p.WaitForExit(3000), "parar fecha o ffmpeg");
        Ok(!Replay.Gravando, "parado");
    }

    [System.Runtime.InteropServices.DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr h);

    static Process FfmpegMaisNovo()
    {
        Process p = null;
        foreach (Process x in Process.GetProcessesByName("ffmpeg"))
            try { if (p == null || x.StartTime > p.StartTime) p = x; } catch { }
        return p;
    }

    // Congela o ffmpeg (como o BlueStacks fez com a captura): o vigia tem de perceber e reiniciar
    static void TestarTravamento()
    {
        ConfigClipe.Segundos = 15;
        ConfigClipe.UsarQualidade(2);
        ConfigClipe.SomPc = true;
        ConfigClipe.Microfone = false;
        ConfigClipe.Ligado = true;
        // Anel vazio: o clipe abaixo tem de ter so o desta sessao e o da seguinte
        foreach (string f in Directory.GetFiles(ConfigClipe.PastaAnel)) try { File.Delete(f); } catch { }
        WindowsFormsSynchronizationContext ctx = new WindowsFormsSynchronizationContext();
        System.Threading.SynchronizationContext.SetSynchronizationContext(ctx);
        Replay.Preparar(ctx);
        Ok(Esperar(delegate { return Replay.Gravando; }, 30000), "replay gravando antes do travamento");
        Esperar(delegate { return false; }, 3000);
        Process velho = FfmpegMaisNovo();
        NtSuspendProcess(velho.Handle);
        Stopwatch sw = Stopwatch.StartNew();
        Ok(Esperar(delegate { return !Replay.Gravando; }, 9000), "vigia percebeu o travamento");
        Console.WriteLine("percebeu em " + sw.ElapsedMilliseconds + " ms");
        Ok(Esperar(delegate { return Replay.Gravando; }, 20000), "replay voltou sozinho");
        Process novo = FfmpegMaisNovo();
        Ok(novo != null && novo.Id != velho.Id && velho.WaitForExit(3000), "ffmpeg travado foi fechado e outro abriu");
        Console.WriteLine("voltou em " + sw.ElapsedMilliseconds + " ms");
        Ok(sw.ElapsedMilliseconds < 6000, "voltou rapido (antes levava ~7 s): " + sw.ElapsedMilliseconds + " ms");
        Esperar(delegate { return false; }, 3000);
        DateTime antesDeSalvar = DateTime.Now;
        Replay.SalvarClipe();
        Esperar(delegate { return !Replay.Salvando; }, 30000);
        // O anel nao recomeca: o clipe tem os ~3 s de antes do travamento e os ~3 s depois
        string feito = null;
        foreach (string f in Directory.GetFiles(ConfigClipe.Pasta, "clipe-*.mp4"))
            if (File.GetLastWriteTime(f) >= antesDeSalvar.AddSeconds(-1)) feito = f;
        Ok(feito != null, "clipe salvo depois do travamento");
        if (feito != null)
        {
            string s = Sondar(feito);
            string d = Miniaturas.LerDuracao(s);
            Console.WriteLine("clipe emendado: " + d);
            Ok(s.Contains("Video: h264") && s.Contains("Audio: aac"), "clipe emendado tem video e som");
            Ok(d != null && string.CompareOrdinal(d, "0:06") >= 0 && string.CompareOrdinal(d, "0:12") <= 0,
               "clipe passa pelas duas sessoes (nao so os 3 s depois): " + d);
            string erros;
            int cod = Ffmpeg.Rodar("-hide_banner -v error -i " + Ffmpeg.Aspas(feito) + " -f null -", null, 60000, out erros);
            Ok(cod == 0 && string.IsNullOrEmpty(erros.Trim()), "clipe emendado decodifica sem erro: " + erros);
        }

        // Perdeu a tela (o que o BlueStacks faz): pelo caminho da linha de erro
        Process antes2 = FfmpegMaisNovo();
        Replay.SimularPerdaDaTela();
        sw.Restart();
        Ok(Esperar(delegate { return Replay.Gravando && FfmpegMaisNovo() != null && FfmpegMaisNovo().Id != antes2.Id; }, 10000), "perdeu a tela e voltou");
        Console.WriteLine("perda da tela: voltou em " + sw.ElapsedMilliseconds + " ms");
        Ok(sw.ElapsedMilliseconds < 3000, "perda da tela volta em menos de 3 s: " + sw.ElapsedMilliseconds + " ms");

        // O pedaco que a sessao derrubada gravava entrou na lista, sem o quadro cortado
        string[] listas = Directory.GetFiles(ConfigClipe.PastaAnel, "lista_s*.csv");
        bool salvouOResto = false;
        foreach (string l in listas) if (File.ReadAllText(l).Contains(",0,0")) salvouOResto = true;
        Ok(salvouOResto, "o segundo antes da queda nao se perde");
        Esperar(delegate { return false; }, 2000);
        antesDeSalvar = DateTime.Now;
        Replay.SalvarClipe();
        Esperar(delegate { return !Replay.Salvando; }, 30000);
        feito = null;
        foreach (string f in Directory.GetFiles(ConfigClipe.Pasta, "clipe-*.mp4"))
            if (File.GetLastWriteTime(f) >= antesDeSalvar.AddSeconds(-1)) feito = f;
        Ok(feito != null, "clipe salvo depois da perda da tela");
        if (feito != null)
        {
            string erros;
            int cod = Ffmpeg.Rodar("-hide_banner -v error -i " + Ffmpeg.Aspas(feito) + " -f null -", null, 60000, out erros);
            Ok(cod == 0 && string.IsNullOrEmpty(erros.Trim()), "clipe com a perda da tela decodifica sem erro: " + erros);
            Console.WriteLine("clipe com 3 sessoes: " + Miniaturas.LerDuracao(Sondar(feito)));
        }
        Replay.Parar();
    }

    static void TestarGravacaoNormal(string clipes)
    {
        foreach (string f in Directory.GetFiles(clipes, "gravacao-*")) File.Delete(f);
        Gravacao.Iniciar(false);
        Ok(Esperar(delegate { return Gravacao.Ativa; }, 20000), "gravacao iniciou");
        Esperar(delegate { return false; }, 6000);
        Console.WriteLine("tempo na tela: " + Gravacao.Tempo);
        Gravacao.Parar();
        Ok(Esperar(delegate { return !Gravacao.Parando; }, 30000), "gravacao terminou de salvar");
        string[] g = Directory.GetFiles(clipes, "gravacao-*.mp4");
        Ok(g.Length == 1, "gravacao virou mp4");
        if (g.Length == 1)
        {
            string s = Sondar(g[0]);
            string d = Miniaturas.LerDuracao(s);
            Ok(s.Contains("Video: h264") && s.Contains("Audio: aac"), "gravacao tem video e som");
            Ok(d == "0:05" || d == "0:06" || d == "0:07", "gravacao tem o tempo gravado: " + d);
        }
        Ok(Directory.GetFileSystemEntries(Gravacao.PastaTemp).Length == 0, "nao sobrou parte temporaria");

        // travou no meio: continua numa parte nova e, no fim, sai um video so
        foreach (string f in Directory.GetFiles(clipes, "gravacao-*")) File.Delete(f);
        Gravacao.Iniciar(false);
        Ok(Esperar(delegate { return Gravacao.Ativa; }, 20000), "segunda gravacao iniciou");
        Esperar(delegate { return false; }, 3000);
        NtSuspendProcess(FfmpegMaisNovo().Handle);
        Stopwatch sw = Stopwatch.StartNew();
        Ok(Esperar(delegate { return Gravacao.Parando || !Gravacao.Ativa; }, 9000), "vigia da gravacao percebeu");
        Ok(Esperar(delegate { return Gravacao.Ativa && !Gravacao.Parando; }, 30000), "gravacao continuou sozinha");
        Console.WriteLine("gravacao voltou em " + sw.ElapsedMilliseconds + " ms");
        Ok(sw.ElapsedMilliseconds < 7000, "gravacao volta rapido (nao espera o \"q\" de um ffmpeg congelado): " + sw.ElapsedMilliseconds + " ms");
        Esperar(delegate { return false; }, 3000);
        Gravacao.Parar();
        Esperar(delegate { return !Gravacao.Parando; }, 30000);
        string[] partes = Directory.GetFiles(clipes, "gravacao-*");
        Ok(partes.Length == 1 && partes[0].EndsWith(".mp4"), "um video so, com as duas partes: " + partes.Length);
        if (partes.Length == 1)
        {
            string s = Sondar(partes[0]);
            string d = Miniaturas.LerDuracao(s);
            Ok(d != null && string.CompareOrdinal(d, "0:06") >= 0 && string.CompareOrdinal(d, "0:13") <= 0, "video tem as duas partes: " + d);
            string erros;
            int cod = Ffmpeg.Rodar("-hide_banner -v error -i " + Ffmpeg.Aspas(partes[0]) + " -f null -", null, 60000, out erros);
            Ok(cod == 0 && string.IsNullOrEmpty(erros.Trim()), "gravacao emendada decodifica sem erro: " + erros);
        }
        Ok(Directory.GetFileSystemEntries(Gravacao.PastaTemp).Length == 0, "partes apagadas depois de juntar");
        foreach (string f in Directory.GetFiles(clipes, "gravacao-*")) File.Delete(f);
    }

    // ---- Editor de clipe ----
    static double DuracaoExata(string arq)
    {
        string s = Sondar(arq);
        int i = s.IndexOf("Duration: ");
        TimeSpan t;
        if (i < 0 || !TimeSpan.TryParse(s.Substring(i + 10, 11), System.Globalization.CultureInfo.InvariantCulture, out t)) return -1;
        return t.TotalSeconds;
    }

    // Volume medio do som (dB), pelo volumedetect do ffmpeg
    static double VolumeMedio(string arq)
    {
        string s;
        Ffmpeg.Rodar("-hide_banner -nostdin -i " + Ffmpeg.Aspas(arq) + " -map 0:a -af volumedetect -f null -", null, 60000, out s);
        int i = s.IndexOf("mean_volume: ");
        if (i < 0) return double.NaN;
        string v = s.Substring(i + 13).Split(' ')[0];
        return double.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    static void TestarEditorClipe(string saida)
    {
        string pasta = Path.Combine(saida, "editor");
        if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
        Directory.CreateDirectory(pasta);
        if (Ffmpeg.Caminho == null) { Ok(false, "ffmpeg encontrado"); return; }

        // Um clipe feito como o replay faz: pedacos .ts de 1 s emendados num .mp4
        string seg;
        int c = Ffmpeg.Rodar("-hide_banner -loglevel error -nostdin -y -f lavfi -i testsrc2=s=1280x720:r=60 -f lavfi -i sine=f=440:r=48000 -t 8 " +
                             "-map 0:v -map 1:a -c:v h264_nvenc -preset p4 -tune hq -rc vbr -b:v 20000k -forced-idr 1 -g 60 -pix_fmt yuv420p " +
                             "-c:a aac -b:a 192k -ac 2 -ar 48000 -force_key_frames \"expr:gte(t,n_forced*1)\" -f segment -segment_time 1 " +
                             "-segment_format mpegts p%02d.ts", pasta, 60000, out seg);
        Ok(c == 0, "pedacos do clipe de teste: " + Ffmpeg.Resumo(seg));
        List<string> ps = new List<string>();
        foreach (string f in Directory.GetFiles(pasta, "p*.ts")) ps.Add(Path.GetFileName(f));
        ps.Sort();
        string clipe = Path.Combine(pasta, "clipe-teste.mp4");
        c = Ffmpeg.Rodar("-hide_banner -loglevel error -nostdin -y -i \"concat:" + string.Join("|", ps.ToArray()) + "\" -map 0 -c copy -movflags +faststart clipe-teste.mp4", pasta, 60000, out seg);
        Ok(c == 0 && File.Exists(clipe), "clipe de teste montado");

        // Indice do .mp4
        Stopwatch sw = Stopwatch.StartNew();
        CorteClipe.Info info = CorteClipe.Ler(clipe);
        Console.WriteLine("leu o indice em " + sw.ElapsedMilliseconds + " ms");
        Ok(info != null, "leu o indice do mp4");
        if (info == null) return;
        Console.WriteLine("duracao " + info.Duracao.ToString("0.000") + " s, quadro " + info.Quadro.ToString("0.0000") + ", chaves: " +
                          string.Join(" ", info.Chaves.ConvertAll(x => x.ToString("0.000")).ToArray()));
        Ok(Math.Abs(info.Duracao - 8) < 0.15, "duracao lida: " + info.Duracao);
        Ok(Math.Abs(info.Quadro - 1 / 60.0) < 0.001, "duracao do quadro: " + info.Quadro);
        // O que o ffmpeg acha, decodificando so os quadros-chave
        string sk;
        Ffmpeg.Rodar("-hide_banner -nostdin -skip_frame nokey -i " + Ffmpeg.Aspas(clipe) + " -map 0:v -vf showinfo -f null -", null, 60000, out sk);
        List<double> reais = new List<double>();
        foreach (string l in sk.Split('\n'))
        {
            int i = l.IndexOf("pts_time:");
            if (i < 0) continue;
            reais.Add(double.Parse(l.Substring(i + 9).Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture));
        }
        Console.WriteLine("chaves pelo ffmpeg: " + string.Join(" ", reais.ConvertAll(x => x.ToString("0.000")).ToArray()));
        bool iguais = reais.Count == info.Chaves.Count;
        for (int i = 0; iguais && i < reais.Count; i++) iguais = Math.Abs(reais[i] - info.Chaves[i]) < 0.02;
        Ok(iguais, "quadros-chave batem com os do ffmpeg (" + info.Chaves.Count + ")");
        Ok(CorteClipe.ChaveMaisPerto(info, 2.4) == info.Chaves[2] && CorteClipe.ChaveMaisPerto(info, 2.6) == info.Chaves[3], "encaixa no quadro-chave mais perto");
        Ok(CorteClipe.Ler(Path.Combine(pasta, ps[0])) == null, ".ts nao tem indice (vai recodificar)");

        // Corte sem recodificar
        double ini = info.Chaves[2];
        string corte = CorteClipe.NomeNovo(clipe);
        Ok(Path.GetFileName(corte) == "clipe-teste-corte.mp4", "nome do corte: " + Path.GetFileName(corte));
        sw.Restart();
        string erro = CorteClipe.Exportar(clipe, corte, ini, 5.5, 100, false);
        Console.WriteLine("cortou em " + sw.ElapsedMilliseconds + " ms");
        Ok(erro == null, "cortou sem erro: " + erro);
        double d = DuracaoExata(corte);
        Ok(Math.Abs(d - (5.5 - ini)) < 0.12, "corte tem o trecho escolhido: " + d.ToString("0.00") + " s (esperado " + (5.5 - ini).ToString("0.00") + ")");
        string s = Sondar(corte);
        Ok(s.Contains("Video: h264") && s.Contains("Audio: aac"), "corte tem video e som");
        string er;
        int cod = Ffmpeg.Rodar("-hide_banner -v error -i " + Ffmpeg.Aspas(corte) + " -f null -", null, 60000, out er);
        Ok(cod == 0 && er.Trim().Length == 0, "corte decodifica sem erro: " + er);
        CorteClipe.Info ic = CorteClipe.Ler(corte);
        // o video comeca num quadro-chave (com o pequeno atraso do som, como nos clipes)
        Ok(ic != null && ic.Chaves.Count > 0 && ic.Chaves[0] < 0.1, "corte comeca num quadro-chave: " + (ic == null || ic.Chaves.Count == 0 ? "?" : ic.Chaves[0].ToString("0.000")));
        Ok(NomeNovoSegue(clipe), "segundo corte ganha outro nome");

        // Sem som e volume
        string mudo = Path.Combine(pasta, "mudo.mp4");
        Ok(CorteClipe.Exportar(clipe, mudo, ini, 5.5, 100, true) == null && !Sondar(mudo).Contains("Audio:"), "mudo tira o som");
        string alto = Path.Combine(pasta, "alto.mp4");
        Ok(CorteClipe.Exportar(clipe, alto, ini, 5.5, 150, false) == null, "volume 150% salvou");
        double v1 = VolumeMedio(corte), v2 = VolumeMedio(alto);
        Console.WriteLine("volume: normal " + v1 + " dB, 150% " + v2 + " dB");
        Ok(v2 - v1 > 3 && v2 - v1 < 4.5, "150% fica ~3,5 dB mais alto: " + (v2 - v1).ToString("0.0") + " dB");
        Ok(Math.Abs(DuracaoExata(alto) - (5.5 - ini)) < 0.12, "volume nao mexe no corte");

        // Arquivo .ts (gravacao recuperada): recodifica no ponto exato
        string dets = Path.Combine(pasta, "de-ts.mp4");
        Ok(CorteClipe.Exportar(Path.Combine(pasta, ps[0]), dets, 0.2, 0.8, 100, false) == null, ".ts recodificado");
        double dt = DuracaoExata(dets);
        Ok(Math.Abs(dt - 0.6) < 0.1, ".ts cortado no ponto exato: " + dt.ToString("0.00"));

        // Miniaturas
        sw.Restart();
        Bitmap faixa = CorteClipe.Miniaturas(clipe, info.Duracao, 10, 64);
        Console.WriteLine("miniaturas em " + sw.ElapsedMilliseconds + " ms: " + (faixa == null ? "nada" : faixa.Width + "x" + faixa.Height));
        Ok(faixa != null && faixa.Height == 64 && faixa.Width >= 10 * 110, "faixa com 10 miniaturas");
        if (faixa != null) { faixa.Save(Path.Combine(pasta, "faixa.png")); faixa.Dispose(); }
        Ok(CorteClipe.Tempo(65.25) == "1:05,3" || CorteClipe.Tempo(65.25) == "1:05,2", "tempo na tela: " + CorteClipe.Tempo(65.25));

        // A janela de verdade, fora da tela e sem tirar o foco
        WindowsFormsSynchronizationContext ctx = new WindowsFormsSynchronizationContext();
        System.Threading.SynchronizationContext.SetSynchronizationContext(ctx);
        EditorClipe e = new EditorClipe(clipe, new Rectangle(-4000, 0, 1600, 900));
        e.SemFoco = true;
        e.Show();
        Ok(Esperar(delegate { return e.Pronto; }, 15000), "editor abriu o video");
        if (!e.Pronto) { e.Close(); return; }
        Ok(Math.Abs(e.Linha.Duracao - 8) < 0.15, "editor: duracao " + e.Linha.Duracao);
        Ok(Esperar(delegate { return e.Linha.Faixa != null; }, 15000), "editor: miniaturas chegaram");
        e.Linha.DefinirInicio(1.3);
        Ok(Math.Abs(e.Linha.Inicio - info.Chaves[1]) < 0.001, "editor: inicio encaixou no quadro-chave: " + e.Linha.Inicio);
        // Foto da janela (com o video), para conferir o visual
        e.Linha.DefinirFim(6.5);
        Esperar(delegate { return false; }, 600);
        using (Bitmap foto = e.Foto()) foto.Save(Path.Combine(pasta, "editor.png"));
        // e numa tela em pe (1080 de largura)
        EditorClipe ep = new EditorClipe(clipe, new Rectangle(-4000, 0, 1080, 1880));
        ep.SemFoco = true;
        ep.Show();
        Esperar(delegate { return ep.Pronto && ep.Linha.Faixa != null; }, 15000);
        ep.Linha.DefinirFim(6.5);
        Esperar(delegate { return false; }, 400);
        using (Bitmap foto = ep.Foto()) foto.Save(Path.Combine(pasta, "editor-em-pe.png"));
        ep.Close();
        e.Linha.DefinirFim(0.5);
        Ok(e.Linha.Fim >= e.Linha.Inicio + LinhaDoTempo.Minimo - 0.001, "editor: fim nunca passa do inicio");
        e.Linha.DefinirFim(4.25);
        e.Alternar();
        Esperar(delegate { return false; }, 1200);
        double andou = e.Linha.Posicao;
        e.Alternar();
        Console.WriteLine("tocou ate " + andou.ToString("0.00") + " s");
        Ok(andou > e.Linha.Inicio + 0.6 && andou < e.Linha.Inicio + 2, "editor: tocar anda o cursor: " + andou.ToString("0.00"));
        e.Alternar();
        Esperar(delegate { return false; }, 3500);   // passa do fim: volta ao inicio
        double rodou = e.Linha.Posicao;
        e.Alternar();
        Ok(rodou >= e.Linha.Inicio && rodou <= e.Linha.Fim, "editor: toca em loop so o trecho: " + rodou.ToString("0.00"));
        e.Volume(50);
        e.Salvar(false);
        Ok(Esperar(delegate { return e.IsDisposed || !e.Visible; }, 30000), "editor: salvou e fechou");
        string feito = e.UltimoSalvo;
        Ok(feito != null && File.Exists(feito), "editor: arquivo novo " + (feito == null ? "" : Path.GetFileName(feito)));
        if (feito != null && File.Exists(feito))
            Ok(Math.Abs(DuracaoExata(feito) - (4.25 - info.Chaves[1])) < 0.12, "editor: trecho certo no arquivo: " + DuracaoExata(feito).ToString("0.00"));

        // Substituir o original
        string copia = Path.Combine(pasta, "substituir.mp4");
        File.Copy(clipe, copia);
        EditorClipe e2 = new EditorClipe(copia, new Rectangle(-4000, 0, 1600, 900));
        e2.SemFoco = true;
        e2.Show();
        Ok(Esperar(delegate { return e2.Pronto; }, 15000), "editor 2 abriu");
        e2.Linha.DefinirFim(3.0);
        e2.Salvar(true);
        Ok(Esperar(delegate { return e2.IsDisposed || !e2.Visible; }, 30000), "editor 2 substituiu e fechou");
        Ok(e2.UltimoSalvo == copia && File.Exists(copia) && Math.Abs(DuracaoExata(copia) - 3.0) < 0.12, "original trocado pelo corte: " + DuracaoExata(copia).ToString("0.00"));
        Ok(Directory.GetFiles(pasta, ".*-editando.mp4").Length == 0, "nao sobrou arquivo temporario");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);

    static bool NomeNovoSegue(string clipe)
    {
        string n = CorteClipe.NomeNovo(clipe);
        return Path.GetFileName(n) == "clipe-teste-corte2.mp4";
    }

    // Pisca um quadrado branco no canto da tela principal e toca um bipe de
    // 4 kHz ao mesmo tempo, 3 vezes. O clipe mostra se o som esta no compasso.
    static byte[] Bipe()
    {
        int taxa = 48000, n = taxa * 150 / 1000;
        MemoryStream ms = new MemoryStream();
        BinaryWriter w = new BinaryWriter(ms);
        w.Write(0x46464952); w.Write(36 + n * 2); w.Write(0x45564157); w.Write(0x20746D66); w.Write(16);
        w.Write((short)1); w.Write((short)1); w.Write(taxa); w.Write(taxa * 2); w.Write((short)2); w.Write((short)16);
        w.Write(0x61746164); w.Write(n * 2);
        for (int i = 0; i < n; i++) w.Write((short)(Math.Sin(2 * Math.PI * 4000 * i / taxa) * 9000));
        return ms.ToArray();
    }

    static void TestarSincronia(string saida)
    {
        string clipes = Path.Combine(saida, "sinc");
        if (Directory.Exists(clipes)) Directory.Delete(clipes, true);
        Directory.CreateDirectory(clipes);
        ConfigClipe.Pasta = clipes;
        ConfigClipe.Segundos = 15;
        ConfigClipe.UsarQualidade(2);
        ConfigClipe.Monitor = "";
        ConfigClipe.SomPc = true;
        ConfigClipe.Microfone = false;
        ConfigClipe.Ligado = true;
        WindowsFormsSynchronizationContext ctx = new WindowsFormsSynchronizationContext();
        System.Threading.SynchronizationContext.SetSynchronizationContext(ctx);
        Replay.Preparar(ctx);
        Ok(Esperar(delegate { return Replay.Gravando; }, 30000), "gravando");

        Form f = new Form();
        f.FormBorderStyle = FormBorderStyle.None;
        f.StartPosition = FormStartPosition.Manual;
        f.ShowInTaskbar = false;
        f.TopMost = true;
        f.BackColor = Color.Black;
        Rectangle t = Screen.PrimaryScreen.Bounds;
        f.Bounds = new Rectangle(t.X, t.Y, 240, 240);
        f.Show();
        Esperar(delegate { return false; }, 2500);
        using (MemoryStream ms = new MemoryStream(Bipe()))
        using (System.Media.SoundPlayer sp = new System.Media.SoundPlayer(ms))
        {
            sp.Load();
            for (int i = 0; i < 3; i++)
            {
                f.BackColor = Color.White;
                f.Refresh();
                sp.Play();
                Esperar(delegate { return false; }, 500);
                f.BackColor = Color.Black;
                f.Refresh();
                Esperar(delegate { return false; }, 1500);
            }
        }
        f.Close();
        Replay.SalvarClipe();
        Esperar(delegate { return !Replay.Salvando; }, 30000);
        foreach (string c in Directory.GetFiles(clipes, "*.mp4")) Console.WriteLine("CLIPE=" + c);
        Replay.Parar();
    }

    static void TestarGaleria(string saida, string clipes)
    {
        string prints = Path.Combine(saida, "prints");
        if (Directory.Exists(prints)) Directory.Delete(prints, true);
        Directory.CreateDirectory(prints);
        Color[] cores = { Color.SteelBlue, Color.IndianRed, Color.SeaGreen, Color.Goldenrod, Color.MediumPurple };
        for (int i = 0; i < cores.Length; i++)
        {
            string f = Path.Combine(prints, "captura-" + i + ".png");
            using (Bitmap b = new Bitmap(i == 2 ? 1080 : 1920, i == 2 ? 1920 : 1080))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(cores[i]);
                    g.FillEllipse(Brushes.White, b.Width / 4, b.Height / 4, b.Width / 2, b.Height / 2);
                }
                b.Save(f, ImageFormat.Png);
            }
            File.SetLastWriteTime(f, DateTime.Now.AddDays(i < 2 ? 0 : i < 4 ? -1 : -6).AddMinutes(-i));
        }

        Stopwatch sw = Stopwatch.StartNew();
        Galeria gal = new Galeria(prints, clipes);
        gal.FicarAberta = true;
        gal.Show();
        Console.WriteLine("abrir galeria: " + sw.ElapsedMilliseconds + " ms");
        int esperadas = cores.Length + Directory.GetFiles(clipes, "*.mp4").Length;
        Esperar(delegate { return false; }, 3000);   // miniaturas carregando em fundo
        FotoForm(gal, Path.Combine(saida, "galeria.png"));

        // arrastar um retangulo sobre os dois primeiros cards de "Hoje"
        GradeMidias gr = gal.Grade;
        Ok(gr.Marcadas == 0, "comeca sem selecao");
        Arrastar(gr, new Point(20, 60), new Point(GradeMidias.Margem + 340, 120));
        Console.WriteLine("selecionados no arrasto: " + gr.Marcadas);
        Ok(gr.Marcadas == 2, "arrastar seleciona os dois primeiros");
        FotoForm(gal, Path.Combine(saida, "galeria-selecao.png"));
        gr.SelecionarTudo();
        Ok(gr.Marcadas == esperadas, "selecionar tudo");
        gr.LimparSelecao();
        Ok(gr.Marcadas == 0, "limpar selecao");

        // excluir permanentemente: so os prints de ontem
        gal.SemConfirmar = true;
        int antes = Directory.GetFiles(prints).Length;
        List<Midia> doisPrints = new List<Midia>();
        Arrastar(gr, new Point(20, 60), new Point(GradeMidias.Margem + 340, 120));
        foreach (Midia m in gr.Selecionadas()) if (!m.Video) doisPrints.Add(m);
        gr.LimparSelecao();
        foreach (Midia m in doisPrints) gr.Alternar(m);
        gal.ExcluirSelecionadas(true);
        Ok(Directory.GetFiles(prints).Length == antes - doisPrints.Count && doisPrints.Count > 0, "excluiu permanentemente " + doisPrints.Count);
        foreach (Midia m in doisPrints) Ok(!File.Exists(m.Caminho), "apagado: " + Path.GetFileName(m.Caminho));

        gal.MostrarConfig(true);
        Esperar(delegate { return false; }, 1500);
        FotoForm(gal, Path.Combine(saida, "galeria-config.png"));
        Ok(!gal.IsDisposed, "galeria aberta ate o fim (" + esperadas + " itens)");
        gal.Close();
        Ok(Galeria.Atual == null, "galeria fechou");
    }

    public static void Rodar(string[] args)
    {
        string saida = args[0];
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 1 && args[1] == "720mic")
        {
            string c = Path.Combine(saida, "c720");
            if (Directory.Exists(c)) Directory.Delete(c, true);
            Directory.CreateDirectory(c);
            ConfigClipe.Pasta = c; ConfigClipe.Segundos = 15; ConfigClipe.UsarQualidade(0);
            ConfigClipe.SomPc = true; ConfigClipe.Microfone = true; ConfigClipe.Ligado = true;
            WindowsFormsSynchronizationContext ctx = new WindowsFormsSynchronizationContext();
            System.Threading.SynchronizationContext.SetSynchronizationContext(ctx);
            Replay.Preparar(ctx);
            Ok(Esperar(delegate { return Replay.Gravando; }, 30000), "720p com microfone gravando: " + Replay.StatusLongo);
            Process[] ps = Process.GetProcessesByName("ffmpeg");
            TimeSpan antes = TimeSpan.Zero, depois = TimeSpan.Zero;
            foreach (Process x in ps) try { antes += x.TotalProcessorTime; } catch { }
            Esperar(delegate { return false; }, 5000);
            foreach (Process x in ps) try { x.Refresh(); depois += x.TotalProcessorTime; } catch { }
            Console.WriteLine("720p: ffmpeg usou " + ((depois - antes).TotalMilliseconds / 50.0).ToString("0") + "% de um nucleo");
            Replay.SalvarClipe();
            Esperar(delegate { return !Replay.Salvando; }, 30000);
            string[] fs = Directory.GetFiles(c, "*.mp4");
            Ok(fs.Length == 1, "salvou");
            if (fs.Length == 1)
            {
                string s = Sondar(fs[0]);
                Ok(s.Contains("1280x720") && s.Contains("Audio: aac") && s.Contains("30 fps"), "1280x720 30 fps com som");
                foreach (string l in s.Split('\n')) if (l.Contains("Stream") || l.Contains("Duration")) Console.WriteLine(l.Trim());
            }
            Replay.Parar();
            Console.WriteLine(falhas == 0 ? "TUDO OK" : "FALHAS: " + falhas);
            Environment.Exit(falhas == 0 ? 0 : 1);
        }
        if (args.Length > 1 && args[1] == "editor")
        {
            TestarEditorClipe(saida);
            Console.WriteLine(falhas == 0 ? "TUDO OK" : "FALHAS: " + falhas);
            Environment.Exit(falhas == 0 ? 0 : 1);
        }

        // Desenha o emoji do icone com o motor do programa (para gerar o icone.ico)
        if (args.Length > 2 && args[1] == "emoji")
        {
            foreach (int lado in new[] { 256, 64, 48, 40, 32, 24, 20, 16 })
            {
                float fonte = lado * float.Parse(args.Length > 3 ? args[3] : "0.72", System.Globalization.CultureInfo.InvariantCulture);
                using (Bitmap b = EmojiCor.Criar(args[2], lado, fonte))
                {
                    if (b == null) { Console.WriteLine("nao desenhou " + lado); continue; }
                    b.Save(Path.Combine(saida, "emoji" + lado + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            Environment.Exit(0);
        }

        // Gera o icone.ico do programa (o 🥋 com a faixa preta) e as previas em PNG
        if (args.Length > 1 && args[1] == "icone")
        {
            byte[] ico = Tema.ArquivoIco(new[] { 16, 20, 24, 32, 40, 48, 64, 256 });
            if (ico == null) { Console.WriteLine("nao desenhou"); Environment.Exit(1); }
            File.WriteAllBytes(Path.Combine(saida, "icone.ico"), ico);
            foreach (int l in new[] { 256, 32, 16 })
                using (Bitmap b = Tema.Quimono(l)) b.Save(Path.Combine(saida, "faixa" + l + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            using (Icon i = new Icon(Path.Combine(saida, "icone.ico"), 16, 16)) Console.WriteLine("icone lido de volta: " + i.Width + "x" + i.Height);
            Environment.Exit(0);
        }

        if (args.Length > 1 && args[1] == "sincronia")
        {
            TestarSincronia(saida);
            Environment.Exit(falhas == 0 ? 0 : 1);
        }
        if (args.Length > 1 && args[1] == "clipes")
        {
            TestarClipes(saida);
            Console.WriteLine(falhas == 0 ? "TUDO OK" : "FALHAS: " + falhas);
            Environment.Exit(falhas == 0 ? 0 : 1);
        }

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
        TestarSalvos(saida, nuc);
        nuc.Dispose();
        Console.WriteLine("janela fechou e soltou o Direct2D");

        // reabre depois de liberar
        Bitmap de_novo = EmojiCor.Criar("\u2764\uFE0F", 40, 27f);
        Ok(de_novo != null, "emoji depois de liberar");
        if (de_novo != null) de_novo.Dispose();
        EmojiCor.Liberar();

        TestarEditor(saida);
        TestarRolagemERecorte(saida);
        TestarClipes(saida);

        Console.WriteLine(falhas == 0 ? "TUDO OK" : "FALHAS: " + falhas);
        Environment.Exit(falhas == 0 ? 0 : 1);
    }
}
#endif
