using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace m3u_editor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Lista mestre com absolutamente todos os itens processados
        private List<M3uItem> _allItems = new List<M3uItem>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            // Para facilitar os testes, você pode colar o texto direto ou ler de um arquivo.
            // Vamos abrir uma caixa de diálogo para carregar o arquivo .m3u
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Arquivos M3U (*.m3u;*.m3u8)|*.m3u;*.m3u8|Todos os arquivos (*.*)|*.*";

            if (openFileDialog.ShowDialog() == true)
            {
                string content = File.ReadAllText(openFileDialog.FileName, Encoding.UTF8);
                ParseM3u(content);
            }
        }

        private void ParseM3u(string text)
        {
            _allItems.Clear();

            // Divide o arquivo por quebras de linha limpas
            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            // Regex para capturar os atributos da tag #EXTINF
            Regex regexTvgName = new Regex(@"tvg-name=""([^""]*)""");
            Regex regexTvgLogo = new Regex(@"tvg-logo=""([^""]*)""");
            Regex regexGroupTitle = new Regex(@"group-title=""([^""]*)""");

            // Loop ajustado para o seu formato (onde a URL vem ANTES da tag #EXTINF)
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("#EXTINF:"))
                {
                    string extinfLine = lines[i];
                    string urlLine = string.Empty;

                    // Como a sua URL está fixada acima da tag correspondente:
                    if (i > 0 && (lines[i - 1].StartsWith("http://") || lines[i - 1].StartsWith("https://")))
                    {
                        urlLine = lines[i - 1];
                    }

                    // Captura o nome de exibição após a última vírgula
                    string displayName = extinfLine.Substring(extinfLine.LastIndexOf(',') + 1).Trim();

                    M3uItem item = new M3uItem
                    {
                        TvgName = regexTvgName.Match(extinfLine).Groups[1].Value,
                        TvgLogo = regexTvgLogo.Match(extinfLine).Groups[1].Value,
                        GroupTitle = regexGroupTitle.Match(extinfLine).Groups[1].Value,
                        DisplayName = displayName,
                        Url = urlLine
                    };

                    // Se não houver group-title definido, joga para um grupo genérico
                    if (string.IsNullOrEmpty(item.GroupTitle))
                        item.GroupTitle = "Sem Categoria";

                    _allItems.Add(item);
                }
            }

            PopulateCategories();
            UpdateStatusBar();
        }

        private void PopulateCategories()
        {
            // Agrupa por Categoria e seleciona apenas o primeiro item encontrado de cada para popular a ListBox
            var uniqueCategories = _allItems
                .GroupBy(i => i.GroupTitle)
                .Select(g => g.First())
                .OrderBy(c => c.GroupTitle)
                .ToList();

            ListCategories.ItemsSource = uniqueCategories;

            if (uniqueCategories.Count > 0)
            {
                ListCategories.SelectedIndex = 0; // Seleciona a primeira por padrão
            }
        }

        // Quando o usuário clica em uma categoria na lista da esquerda
        private void ListCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListCategories.SelectedItem is M3uItem selectedCategory)
            {
                // Filtra a lista principal trazendo apenas os itens que pertencem à categoria selecionada
                var filteredItems = _allItems
                    .Where(i => i.GroupTitle == selectedCategory.GroupTitle)
                    .ToList();

                GridItems.ItemsSource = filteredItems;
                UpdateStatusBar();
            }
        }

        // Gera o arquivo M3U atualizado com as modificações feitas no DataGrid
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (!_allItems.Any())
            {
                MessageBox.Show("Não há dados para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Arquivo M3U (*.m3u)|*.m3u";
            if (saveFileDialog.ShowDialog() == true)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("#EXTM3U");

                foreach (var item in _allItems)
                {
                    // Mantém a ordem original do seu exemplo (URL acima do metadado)
                    if (!string.IsNullOrEmpty(item.Url))
                    {
                        sb.AppendLine(item.Url);
                    }
                    sb.AppendLine($"#EXTINF:-1 tvg-name=\"{item.TvgName}\" tvg-logo=\"{item.TvgLogo}\" group-title=\"{item.GroupTitle}\",{item.DisplayName}");
                }

                File.WriteAllText(saveFileDialog.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("Lista M3U exportada com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            // Verifica se existem itens selecionados no DataGrid
            if (GridItems.SelectedItems.Count == 0) return;

            // Confirmação para evitar exclusões acidentais
            var result = MessageBox.Show(
                $"Tem certeza que deseja excluir os {GridItems.SelectedItems.Count} itens selecionados?",
                "Confirmar Exclusão",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Criamos uma lista temporária convertendo os itens selecionados para o tipo M3uItem
                List<M3uItem> itemsToRemove = GridItems.SelectedItems.Cast<M3uItem>().ToList();

                // Remove os itens da nossa lista mestre (de onde o M3U final é gerado)
                foreach (var item in itemsToRemove)
                {
                    _allItems.Remove(item);
                }

                // Armazena qual categoria estava selecionada para não perder o foco do usuário
                var selectedCategory = ListCategories.SelectedItem as M3uItem;

                // Recarrega as categorias da esquerda (caso o usuário tenha deletado TODOS os itens de uma categoria, ela sumirá da lista)
                PopulateCategories();

                // Se a categoria ainda existir, mantém ela selecionada e atualiza a grid da direita
                if (selectedCategory != null)
                {
                    // Tenta re-selecionar a categoria correspondente pelo nome do grupo
                    var stillExists = ListCategories.Items.Cast<M3uItem>().FirstOrDefault(c => c.GroupTitle == selectedCategory.GroupTitle);
                    if (stillExists != null)
                    {
                        ListCategories.SelectedItem = stillExists;

                        // Força a atualização visual do DataGrid com os itens restantes
                        var filteredItems = _allItems.Where(i => i.GroupTitle == stillExists.GroupTitle).ToList();
                        GridItems.ItemsSource = filteredItems;
                        UpdateStatusBar();
                    }
                    else
                    {
                        // Se a categoria inteira sumiu porque limpamos ela, limpa o DataGrid
                        GridItems.ItemsSource = null;
                        UpdateStatusBar();
                    }
                }
            }
        }

        private void GridItems_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                MenuDelete_Click(sender, e);
                e.Handled = true; // Evita que o WPF tente processar a tecla deletar internamente
            }
        }

        private void MenuDeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            // Verifica se existe alguma categoria selecionada
            if (ListCategories.SelectedItem is M3uItem selectedCategory)
            {
                string categoryName = selectedCategory.GroupTitle;

                // Conta quantos itens serão afetados para avisar o usuário
                int itemsCount = _allItems.Count(i => i.GroupTitle == categoryName);

                // Confirmação de segurança
                var result = MessageBox.Show(
                    $"Tem certeza que deseja excluir a categoria \"{categoryName}\" e todos os seus {itemsCount} itens?",
                    "Confirmar Exclusão de Categoria",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    // Remove da lista mestre todos os itens que pertencem a essa categoria
                    _allItems.RemoveAll(i => i.GroupTitle == categoryName);

                    // Limpa o DataGrid da direita, já que os itens exibidos foram apagados
                    GridItems.ItemsSource = null;

                    // Recarrega a lista de categorias da esquerda
                    PopulateCategories();
                    UpdateStatusBar();

                    MessageBox.Show($"Categoria \"{categoryName}\" excluída com sucesso.", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("Por favor, selecione uma categoria para excluir.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateStatusBar()
        {
            // 1. Atualiza os contadores numéricos
            TxtTotalItems.Text = _allItems.Count.ToString();

            if (GridItems.ItemsSource is System.Collections.ICollection currentCategoryList)
            {
                TxtCategoryItems.Text = currentCategoryList.Count.ToString();
            }
            else
            {
                TxtCategoryItems.Text = "0";
            }

            // 2. Calcula o tamanho em bytes simulando a escrita do arquivo final em UTF-8
            if (_allItems.Count == 0)
            {
                TxtFileSize.Text = "0 bytes";
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("#EXTM3U");
            foreach (var item in _allItems)
            {
                if (!string.IsNullOrEmpty(item.Url))
                {
                    sb.AppendLine(item.Url);
                }
                sb.AppendLine($"#EXTINF:-1 tvg-name=\"{item.TvgName}\" tvg-logo=\"{item.TvgLogo}\" group-title=\"{item.GroupTitle}\",{item.DisplayName}");
            }

            // Obtém o total de bytes baseado na string completa gerada
            long byteCount = Encoding.UTF8.GetByteCount(sb.ToString());

            // Formata a exibição do tamanho amigavelmente (Bytes, KB ou MB)
            if (byteCount < 1024)
                TxtFileSize.Text = $"{byteCount} bytes";
            else if (byteCount < 1024 * 1024)
                TxtFileSize.Text = $"{(byteCount / 1024.0):F2} KB";
            else
                TxtFileSize.Text = $"{(byteCount / (1024.0 * 1024.0)):F2} MB";
        }

        private void GridItems_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Verifica se o clique foi realmente em uma linha preenchida e obtém o item
            if (GridItems.SelectedItem is M3uItem selectedItem)
            {
                if (!string.IsNullOrEmpty(selectedItem.Url))
                {
                    try
                    {
                        // Dispara a URL usando o programa padrão do Windows (VLC, Media Player, etc.)
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = selectedItem.Url,
                            UseShellExecute = true // Obrigatório no .NET Core/WPF moderno para abrir URLs
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Não foi possível reproduzir a URL: {ex.Message}", "Erro ao abrir Player", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Este item não possui uma URL válida para reprodução.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}