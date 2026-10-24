using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RustPlusDesk.Services.AiCompanion;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views.Windows
{
    public partial class OpenRouterModelPickerWindow : WpfUi.FluentWindow
    {
        public string? SelectedModelId { get; private set; }

        private IReadOnlyList<OpenRouterModelInfo> _allModels = Array.Empty<OpenRouterModelInfo>();
        private OpenRouterModelInfo? _currentSelectedModel;
        private bool _isInitializing = true;

        public OpenRouterModelPickerWindow(string? initialModelId = null)
        {
            InitializeComponent();
            SelectedModelId = initialModelId;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            PopulateProvidersDropdown();
            await LoadModelsAsync();
            _isInitializing = false;
        }

        private void PopulateProvidersDropdown()
        {
            CmbProviderFilter.Items.Clear();
            CmbProviderFilter.Items.Add(new ComboBoxItem { Content = "All Providers", Tag = "All" });
            CmbProviderFilter.Items.Add(new ComboBoxItem { Content = "OpenAI", Tag = "openai" });
            CmbProviderFilter.Items.Add(new ComboBoxItem { Content = "Anthropic", Tag = "anthropic" });
            CmbProviderFilter.Items.Add(new ComboBoxItem { Content = "Google", Tag = "google" });
            CmbProviderFilter.Items.Add(new ComboBoxItem { Content = "DeepSeek", Tag = "deepseek" });
            CmbProviderFilter.Items.Add(new ComboBoxItem { Content = "Meta Llama", Tag = "meta-llama" });
            CmbProviderFilter.Items.Add(new ComboBoxItem { Content = "Mistral", Tag = "mistralai" });
            CmbProviderFilter.Items.Add(new ComboBoxItem { Content = "Qwen", Tag = "qwen" });
            CmbProviderFilter.Items.Add(new ComboBoxItem { Content = "NVIDIA", Tag = "nvidia" });
            CmbProviderFilter.SelectedIndex = 0;
        }

        private async Task LoadModelsAsync(bool forceRefresh = false)
        {
            PanelLoading.Visibility = Visibility.Visible;
            TxtEmpty.Visibility = Visibility.Collapsed;
            LstModels.Visibility = Visibility.Collapsed;
            TxtStatus.Text = "Fetching models from OpenRouter...";

            try
            {
                _allModels = await OpenRouterModelService.GetModelsAsync(forceRefresh);
                ApplyFilter();

                // Select initial model if present
                if (!string.IsNullOrWhiteSpace(SelectedModelId))
                {
                    var match = _allModels.FirstOrDefault(m => string.Equals(m.Id, SelectedModelId, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        LstModels.SelectedItem = match;
                        LstModels.ScrollIntoView(match);
                    }
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Failed to load models: {ex.Message}";
            }
            finally
            {
                PanelLoading.Visibility = Visibility.Collapsed;
                LstModels.Visibility = Visibility.Visible;
            }
        }

        private void ApplyFilter()
        {
            if (_allModels.Count == 0)
            {
                TxtEmpty.Visibility = Visibility.Visible;
                LstModels.ItemsSource = null;
                TxtStatus.Text = "No models available.";
                UpdateDetailsView(null);
                return;
            }

            bool? freeOnly = null;
            if (RadFilterFree.IsChecked == true) freeOnly = true;
            else if (RadFilterPaid.IsChecked == true) freeOnly = false;

            bool? visionOnly = ChkFilterVision.IsChecked == true ? true : null;

            string? provider = (CmbProviderFilter.SelectedItem as ComboBoxItem)?.Tag as string;
            string query = TxtSearch.Text ?? "";

            var filtered = OpenRouterModelService.SearchModels(_allModels, query, freeOnly, provider, visionOnly).ToList();

            LstModels.ItemsSource = filtered;
            TxtEmpty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            int freeCount = _allModels.Count(m => m.IsFree);
            TxtStatus.Text = $"Showing {filtered.Count} of {_allModels.Count} models ({freeCount} free)";

            if (filtered.Count > 0)
            {
                var target = filtered.FirstOrDefault(m => string.Equals(m.Id, _currentSelectedModel?.Id, StringComparison.OrdinalIgnoreCase)) ?? filtered[0];
                LstModels.SelectedItem = target;
            }
            else
            {
                UpdateDetailsView(null);
            }
        }

        private void LstModels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstModels.SelectedItem is OpenRouterModelInfo model)
            {
                UpdateDetailsView(model);
            }
            else
            {
                UpdateDetailsView(null);
            }
        }

        private void UpdateDetailsView(OpenRouterModelInfo? model)
        {
            _currentSelectedModel = model;

            if (model == null)
            {
                PanelNoSelection.Visibility = Visibility.Visible;
                PanelModelDetails.Visibility = Visibility.Collapsed;
                return;
            }

            PanelNoSelection.Visibility = Visibility.Collapsed;
            PanelModelDetails.Visibility = Visibility.Visible;

            TxtDetailName.Text = model.Name;
            TxtDetailProvider.Text = $"Provider: {model.ProviderName}";
            TxtDetailId.Text = model.Id;

            BadgeFree.Visibility = model.IsFree ? Visibility.Visible : Visibility.Collapsed;
            BadgePaid.Visibility = !model.IsFree ? Visibility.Visible : Visibility.Collapsed;
            BadgeVision.Visibility = model.SupportsVision ? Visibility.Visible : Visibility.Collapsed;
            BadgeTextOnly.Visibility = !model.SupportsVision ? Visibility.Visible : Visibility.Collapsed;

            TxtDetailContext.Text = model.FormattedContextLength;
            TxtDetailMaxOutput.Text = model.FormattedMaxOutput;
            TxtDetailModality.Text = model.Modality;

            if (!string.IsNullOrEmpty(model.Tokenizer))
            {
                RowTokenizer.Visibility = Visibility.Visible;
                TxtDetailTokenizer.Text = model.Tokenizer;
            }
            else
            {
                RowTokenizer.Visibility = Visibility.Collapsed;
            }

            TxtDetailModerated.Text = model.IsModerated ? "Yes (Safe/Filtered)" : "No";

            TxtDetailPromptPrice.Text = model.IsFree ? "$0.00 (Free)" : $"{model.FormattedPromptPricePerMillion} / 1M";
            TxtDetailCompletionPrice.Text = model.IsFree ? "$0.00 (Free)" : $"{model.FormattedCompletionPricePerMillion} / 1M";

            if (model.ImagePrice > 0)
            {
                RowImagePrice.Visibility = Visibility.Visible;
                TxtDetailImagePrice.Text = model.FormattedImagePrice + " / image";
            }
            else
            {
                RowImagePrice.Visibility = Visibility.Collapsed;
            }

            TxtDetailDescription.Text = string.IsNullOrWhiteSpace(model.Description)
                ? "No detailed description provided by OpenRouter."
                : model.Description;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            ApplyFilter();
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplyFilter();
        }

        private void CmbProviderFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            ApplyFilter();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadModelsAsync(forceRefresh: true);
        }

        private void BtnCopyId_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSelectedModel != null)
            {
                try
                {
                    Clipboard.SetText(_currentSelectedModel.Id);
                }
                catch { }
            }
        }

        private void BtnSelectModel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string modelId })
            {
                SelectAndClose(modelId);
            }
        }

        private void BtnUseSelectedModel_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSelectedModel != null)
            {
                SelectAndClose(_currentSelectedModel.Id);
            }
        }

        private void LstModels_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstModels.SelectedItem is OpenRouterModelInfo model)
            {
                SelectAndClose(model.Id);
            }
        }

        private void SelectAndClose(string modelId)
        {
            SelectedModelId = modelId;
            DialogResult = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
