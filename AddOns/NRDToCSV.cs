#region Using declarations
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Xml.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Gui.Tools;
#endregion

namespace NinjaTrader.Gui.NinjaScript
{
    public class NRDToCSV : AddOnBase
    {
        private NTMenuItem menuItem;
        private NTMenuItem existingMenuItemInControlCenter;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NRDToCSV";
                Description = "*.nrd to *.csv market replay files convertion";
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            ControlCenter cc = window as ControlCenter;
            if (cc == null) return;

            existingMenuItemInControlCenter = cc.FindFirst("ControlCenterMenuItemTools") as NTMenuItem;
            if (existingMenuItemInControlCenter == null) return;

            menuItem = new NTMenuItem { Header = "NRD to CSV", Style = Application.Current.TryFindResource("MainMenuItem") as Style };
            existingMenuItemInControlCenter.Items.Add(menuItem);
            menuItem.Click += OnMenuItemClick;
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (menuItem != null && window is ControlCenter)
            {
                if (existingMenuItemInControlCenter != null && existingMenuItemInControlCenter.Items.Contains(menuItem))
                    existingMenuItemInControlCenter.Items.Remove(menuItem);
                menuItem.Click -= OnMenuItemClick;
                menuItem = null;
            }
        }

        private void OnMenuItemClick(object sender, RoutedEventArgs e)
        {
            Core.Globals.RandomDispatcher.BeginInvoke(new Action(() => new NRDToCSVWindow().Show()));
        }
    }

    public class NRDToCSVWindow : NTWindow, IWorkspacePersistence
    {
        private static readonly int PARALLEL_THREADS_COUNT = 4;
        private TextBox tbCsvRootDir;
        private Label lCsvRootDir;
        private Label lDateRange;
        private Label lHistoricalSeries;
        private ComboBox cbExportMode;
        private ComboBox cbHistoricalSeries;
        private DatePicker dpStartDate;
        private DatePicker dpEndDate;
        private ComboBox cbInstrumentFilter;
        private Button bConvert;
        private TextBox tbOutput;
        private Label lProgress;
        private ProgressBar pbProgress;
        private int taskCount;
        private DateTime startTimestamp;
        private long completeFilesLength;
        private long totalFilesLength;
        private bool showByteProgress;
        private bool running;
        private bool canceling;

        public NRDToCSVWindow()
        {
            Caption = "NRD to CSV";
            Width = 512;
            Height = 512;
            Content = BuildContent();
            Loaded += (o, e) =>
            {
                if (WorkspaceOptions == null)
                    WorkspaceOptions = new WorkspaceOptions("NRDToCSV-" + Guid.NewGuid().ToString("N"), this);
                UpdateModeUiState();
                RefreshInstrumentFilterOptions();
            };
            Closing += (o, e) =>
            {
                if (bConvert != null)
                    bConvert.Click -= OnConvertButtonClick;
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            if (running)
                canceling = true;
            base.OnClosed(e);
        }


        private DependencyObject BuildContent()
        {
            double margin = (double)FindResource("MarginBase");
            tbCsvRootDir = new TextBox()
            {
                Margin = new Thickness(margin, 0, margin, margin),
                Text = Path.Combine(Globals.UserDataDir, "db", "replay.csv"),
            };
            lCsvRootDir = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, 0, margin, 0),
                Content = "Root directory of converted CSV files:",
            };
            Label lExportMode = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, margin, margin, 0),
                Content = "Export mode:",
            };
            cbExportMode = new ComboBox()
            {
                Margin = new Thickness(margin, 0, margin, margin),
                ItemsSource = new[]
                {
                    new OptionItem<ExportMode>(ExportMode.Replay, "Replay top-of-book/trades (.nrd -> L1 CSV)"),
                    new OptionItem<ExportMode>(ExportMode.AuditReplayCoverage, "Audit replay coverage (.nrd -> CSV/TXT report)"),
                    new OptionItem<ExportMode>(ExportMode.Historical, "Historical bars (.ncd -> CSV)"),
                },
                SelectedIndex = 0,
            };
            cbExportMode.SelectionChanged += OnExportSettingsChanged;
            lHistoricalSeries = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, 0, margin, 0),
                Content = "Historical series:",
            };
            cbHistoricalSeries = new ComboBox()
            {
                Margin = new Thickness(margin, 0, margin, margin),
                ItemsSource = new[]
                {
                    new OptionItem<HistoricalSeriesKind>(HistoricalSeriesKind.Tick, "Tick (1 tick, local repository)"),
                    new OptionItem<HistoricalSeriesKind>(HistoricalSeriesKind.Minute, "Minute (1 minute, local repository)"),
                    new OptionItem<HistoricalSeriesKind>(HistoricalSeriesKind.Day, "Day (1 day, local repository)"),
                },
                SelectedIndex = 0,
            };
            cbHistoricalSeries.SelectionChanged += OnExportSettingsChanged;
            lDateRange = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, 0, margin, 0),
                Content = "Data date range to convert (optional):",
            };
            dpStartDate = new DatePicker()
            {
                Margin = new Thickness(0, 0, margin / 2, 0),
                SelectedDateFormat = DatePickerFormat.Short,
                MinWidth = 120,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            dpEndDate = new DatePicker()
            {
                Margin = new Thickness(margin / 2, 0, 0, 0),
                SelectedDateFormat = DatePickerFormat.Short,
                MinWidth = 120,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            ConfigureDatePicker(dpStartDate);
            ConfigureDatePicker(dpEndDate);
            Label lDateRangeTo = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = "to",
            };
            Grid gDateRange = new Grid() { Margin = new Thickness(margin, 0, margin, margin) };
            gDateRange.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            gDateRange.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            gDateRange.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            Grid.SetColumn(dpStartDate, 0);
            Grid.SetColumn(lDateRangeTo, 1);
            Grid.SetColumn(dpEndDate, 2);
            gDateRange.Children.Add(dpStartDate);
            gDateRange.Children.Add(lDateRangeTo);
            gDateRange.Children.Add(dpEndDate);
            Label lInstrumentFilter = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Margin = new Thickness(margin, 0, margin, 0),
                Content = "Instrument filter (optional, e.g. MNQ or NQ):",
            };
            cbInstrumentFilter = new ComboBox()
            {
                Margin = new Thickness(margin, 0, margin, margin),
                IsEditable = true,
                IsTextSearchEnabled = false,
                StaysOpenOnEdit = true,
                ToolTip = "Leave blank to process all instruments. Enter a base instrument such as MNQ or NQ, or a full contract name.",
            };
            bConvert = new Button() { Margin = new Thickness(margin), IsDefault = true, Content = "_Convert" };
            bConvert.Click += OnConvertButtonClick;
            tbOutput = new TextBox()
            {
                IsReadOnly = true,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(margin),
            };
            pbProgress = new ProgressBar()
            {
                Height = 0,
            };
            lProgress = new Label()
            {
                Foreground = FindResource("FontLabelBrush") as Brush,
                Height = 0,
            };

            Grid grid = new Grid() { Background = new SolidColorBrush(Colors.Transparent) };
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            Grid.SetRow(lCsvRootDir, 0);
            Grid.SetRow(tbCsvRootDir, 1);
            Grid.SetRow(lExportMode, 2);
            Grid.SetRow(cbExportMode, 3);
            Grid.SetRow(lHistoricalSeries, 4);
            Grid.SetRow(cbHistoricalSeries, 5);
            Grid.SetRow(lDateRange, 6);
            Grid.SetRow(gDateRange, 7);
            Grid.SetRow(lInstrumentFilter, 8);
            Grid.SetRow(cbInstrumentFilter, 9);
            Grid.SetRow(bConvert, 10);
            Grid.SetRow(tbOutput, 11);
            Grid.SetRow(lProgress, 12);
            Grid.SetRow(pbProgress, 13);
            grid.Children.Add(lCsvRootDir);
            grid.Children.Add(tbCsvRootDir);
            grid.Children.Add(lExportMode);
            grid.Children.Add(cbExportMode);
            grid.Children.Add(lHistoricalSeries);
            grid.Children.Add(cbHistoricalSeries);
            grid.Children.Add(lDateRange);
            grid.Children.Add(gDateRange);
            grid.Children.Add(lInstrumentFilter);
            grid.Children.Add(cbInstrumentFilter);
            grid.Children.Add(bConvert);
            grid.Children.Add(tbOutput);
            grid.Children.Add(lProgress);
            grid.Children.Add(pbProgress);
            UpdateModeUiState();
            return grid;
        }

        private void OnExportSettingsChanged(object sender, SelectionChangedEventArgs e)
        {
            if (running)
                return;

            UpdateModeUiState();
            RefreshInstrumentFilterOptions();
        }

        private void ConfigureDatePicker(DatePicker datePicker)
        {
            if (datePicker == null)
                return;

            datePicker.Padding = new Thickness(0);
            datePicker.BorderThickness = new Thickness(0);
            datePicker.BorderBrush = Brushes.Transparent;
            datePicker.Background = Brushes.Transparent;
            datePicker.Loaded += OnDatePickerLoaded;
        }

        private void OnDatePickerLoaded(object sender, RoutedEventArgs e)
        {
            DatePicker datePicker = sender as DatePicker;
            if (datePicker == null)
                return;

            datePicker.ApplyTemplate();

            Brush fieldBackground = tbCsvRootDir != null ? tbCsvRootDir.Background : null;
            Brush fieldBorderBrush = tbCsvRootDir != null ? tbCsvRootDir.BorderBrush : null;
            Brush fieldForeground = tbCsvRootDir != null ? tbCsvRootDir.Foreground : null;
            Thickness fieldBorderThickness = tbCsvRootDir != null ? tbCsvRootDir.BorderThickness : new Thickness(1);

            DatePickerTextBox textBox = datePicker.Template.FindName("PART_TextBox", datePicker) as DatePickerTextBox;
            if (textBox != null)
            {
                if (fieldBackground != null)
                    textBox.Background = fieldBackground;
                if (fieldBorderBrush != null)
                    textBox.BorderBrush = fieldBorderBrush;
                if (fieldForeground != null)
                    textBox.Foreground = fieldForeground;
                textBox.BorderThickness = fieldBorderThickness;
                textBox.Padding = new Thickness(4, 2, 2, 2);
                textBox.Margin = new Thickness(0);
                textBox.MinWidth = 0;
                textBox.TextAlignment = TextAlignment.Center;
                textBox.VerticalContentAlignment = VerticalAlignment.Center;
            }

            Button button = datePicker.Template.FindName("PART_Button", datePicker) as Button;
            if (button != null)
            {
                button.Margin = new Thickness(-2, 2, 0, 0);
                button.Padding = new Thickness(0);
                button.MinWidth = 22;
                button.Width = 22;
            }
        }

        private void UpdateModeUiState()
        {
            ExportMode exportMode = GetSelectedExportMode();
            bool isHistorical = exportMode == ExportMode.Historical;
            bool isAudit = exportMode == ExportMode.AuditReplayCoverage;
            string defaultCsvRootDir = Path.Combine(Globals.UserDataDir, "db", "replay.csv");
            string defaultAuditOutputDir = Path.Combine(Globals.UserDataDir, "db", "replay-audit");
            if (tbCsvRootDir != null)
            {
                if (isAudit && string.Equals(tbCsvRootDir.Text, defaultCsvRootDir, StringComparison.OrdinalIgnoreCase))
                    tbCsvRootDir.Text = defaultAuditOutputDir;
                else if (!isAudit && string.Equals(tbCsvRootDir.Text, defaultAuditOutputDir, StringComparison.OrdinalIgnoreCase))
                    tbCsvRootDir.Text = defaultCsvRootDir;
            }
            if (lCsvRootDir != null)
                lCsvRootDir.Content = isAudit
                    ? "Output directory for replay audit reports:"
                    : "Root directory of converted CSV files:";
            if (lDateRange != null)
            {
                if (isHistorical)
                    lDateRange.Content = "Historical data date range to export:";
                else if (isAudit)
                    lDateRange.Content = "Replay coverage audit date range (optional; needed to report missing days before/after existing files):";
                else
                    lDateRange.Content = "Replay CSV date range to produce (optional):";
            }

            if (lHistoricalSeries != null)
                lHistoricalSeries.Visibility = isHistorical ? Visibility.Visible : Visibility.Collapsed;
            if (cbHistoricalSeries != null)
                cbHistoricalSeries.Visibility = isHistorical ? Visibility.Visible : Visibility.Collapsed;
            if (bConvert != null && !running)
                bConvert.Content = isAudit ? "_Audit" : "_Convert";
        }

        private ExportMode GetSelectedExportMode()
        {
            OptionItem<ExportMode> selected = cbExportMode != null ? cbExportMode.SelectedItem as OptionItem<ExportMode> : null;
            return selected != null ? selected.Value : ExportMode.Replay;
        }

        private HistoricalSeriesKind GetSelectedHistoricalSeriesKind()
        {
            OptionItem<HistoricalSeriesKind> selected = cbHistoricalSeries != null ? cbHistoricalSeries.SelectedItem as OptionItem<HistoricalSeriesKind> : null;
            return selected != null ? selected.Value : HistoricalSeriesKind.Tick;
        }

        private void SelectComboValue<T>(ComboBox comboBox, T value)
        {
            if (comboBox == null || comboBox.ItemsSource == null)
                return;

            foreach (object item in comboBox.ItemsSource)
            {
                OptionItem<T> option = item as OptionItem<T>;
                if (option != null && EqualityComparer<T>.Default.Equals(option.Value, value))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        private T ParseEnumValue<T>(string value, T fallback) where T : struct
        {
            T parsed;
            return Enum.TryParse(value, true, out parsed) ? parsed : fallback;
        }

        private void OnConvertButtonClick(object sender, RoutedEventArgs e)
        {
            if (tbOutput == null) return;

            if (running)
            {
                if (!canceling)
                {
                    canceling = true;
                    logout(GetSelectedExportMode() == ExportMode.AuditReplayCoverage ? "Canceling audit..." : "Canceling conversion...");
                    bConvert.IsEnabled = false;
                    bConvert.Content = "Canceling...";
                }
                return;
            }

            tbOutput.Clear();

            ExportMode exportMode = GetSelectedExportMode();
            string dataRootDir = GetDataRootDirectory(exportMode);
            string csvDir = tbCsvRootDir.Text;
            DateTime? selectedStartDate = dpStartDate.SelectedDate?.Date;
            DateTime? selectedEndDate = dpEndDate.SelectedDate?.Date;
            HashSet<string> selectedInstruments = ParseInstrumentFilter();

            if (selectedStartDate.HasValue && selectedEndDate.HasValue && selectedStartDate.Value > selectedEndDate.Value)
            {
                logout("ERROR: Start date must be before or equal to end date");
                return;
            }

            if (exportMode == ExportMode.Historical && (!selectedStartDate.HasValue || !selectedEndDate.HasValue))
            {
                logout("ERROR: Historical export requires both a start date and an end date");
                return;
            }

            if (!Directory.Exists(dataRootDir))
            {
                logout(string.Format("ERROR: The data root directory \"{0}\" not found", dataRootDir));
                return;
            }

            string[] instrumentDirs = Directory.GetDirectories(dataRootDir);
            RefreshInstrumentFilterOptions(instrumentDirs);
            if (instrumentDirs.Length == 0)
            {
                logout(string.Format("WARNING: The data root directory \"{0}\" is empty", dataRootDir));
                return;
            }

            if (!Directory.Exists(csvDir))
            {
                try
                {
                    Directory.CreateDirectory(csvDir);
                }
                catch (Exception error)
                {
                    logout(string.Format("ERROR: Unable to create the CSV root directory \"{0}\": {1}", csvDir, error.ToString()));
                }
                return;
            }

            if (exportMode == ExportMode.Historical)
                StartHistoricalExport(csvDir, instrumentDirs, selectedStartDate.Value, selectedEndDate.Value, selectedInstruments);
            else if (exportMode == ExportMode.AuditReplayCoverage)
                StartReplayAudit(dataRootDir, csvDir, instrumentDirs, selectedStartDate, selectedEndDate, selectedInstruments);
            else
                StartReplayExport(dataRootDir, csvDir, instrumentDirs, selectedStartDate, selectedEndDate, selectedInstruments);
        }

        private string GetDataRootDirectory(ExportMode exportMode)
        {
            if (exportMode == ExportMode.Historical)
                return Path.Combine(Globals.UserDataDir, "db", GetHistoricalFolderName(GetSelectedHistoricalSeriesKind()));

            return Path.Combine(Globals.UserDataDir, "db", "replay");
        }

        private string GetHistoricalFolderName(HistoricalSeriesKind seriesKind)
        {
            switch (seriesKind)
            {
                case HistoricalSeriesKind.Day:
                    return "day";
                case HistoricalSeriesKind.Minute:
                    return "minute";
                default:
                    return "tick";
            }
        }

        private void StartReplayExport(string nrdRoot, string csvDir, IEnumerable<string> instrumentDirs, DateTime? selectedStartDate, DateTime? selectedEndDate, HashSet<string> selectedInstruments)
        {
            Globals.RandomDispatcher.InvokeAsync(new Action(() =>
            {
                completeFilesLength = 0;
                totalFilesLength = 0;
                List<DumpEntry> entries = new List<DumpEntry>();
                foreach (string subDir in instrumentDirs)
                    ProceedDirectory(entries, nrdRoot, subDir, csvDir, selectedStartDate, selectedEndDate, selectedInstruments);
                if (entries.Count == 0)
                {
                    if (selectedInstruments.Count > 0)
                        logout(string.Format("No *.nrd files found to convert for instrument filter \"{0}\"", cbInstrumentFilter.Text));
                    else
                        logout("No *.nrd files found to convert");
                }
                else
                {
                    Globals.RandomDispatcher.InvokeAsync(new Action(() =>
                    {
                        logout(string.Format("Convert {0} files...", entries.Count));
                        run(entries.Count, true);
                        taskCount = PARALLEL_THREADS_COUNT;
                        for (int i = 0; i < taskCount; i++)
                            RunReplayConversion(entries, i, taskCount);
                    }));
                }
            }));
        }

        private void StartReplayAudit(string nrdRoot, string outputDir, IEnumerable<string> instrumentDirs, DateTime? selectedStartDate, DateTime? selectedEndDate, HashSet<string> selectedInstruments)
        {
            Globals.RandomDispatcher.InvokeAsync(new Action(() =>
            {
                List<ReplayContractFolder> folders = BuildReplayAuditFolders(instrumentDirs, selectedInstruments);
                if (folders.Count == 0)
                {
                    if (selectedInstruments.Count > 0)
                        logout(string.Format("No replay contract folders found for instrument filter \"{0}\"", cbInstrumentFilter.Text));
                    else
                        logout("No replay contract folders found to audit");
                    return;
                }

                List<ReplayAuditEntry> entries = BuildReplayAuditEntries(folders, selectedStartDate, selectedEndDate, selectedInstruments);
                if (entries.Count == 0)
                {
                    logout("No replay dates found to audit. Select a date range to report missing replay days.");
                    return;
                }

                logout(string.Format("Audit {0} replay date/file rows...", entries.Count));
                run(entries.Count, false);
                RunReplayAudit(nrdRoot, outputDir, entries, selectedStartDate, selectedEndDate, selectedInstruments);
            }));
        }

        private List<ReplayContractFolder> BuildReplayAuditFolders(IEnumerable<string> instrumentDirs, HashSet<string> selectedInstruments)
        {
            List<ReplayContractFolder> folders = new List<ReplayContractFolder>();
            foreach (string instrumentDir in instrumentDirs)
            {
                string fullName = Path.GetFileName(instrumentDir);
                if (string.IsNullOrWhiteSpace(fullName))
                    continue;

                Collection<Instrument> instruments = InstrumentList.GetInstruments(fullName);
                Cbi.Instrument instrument = instruments.Count == 1 ? instruments[0] : null;
                string baseInstrument = instrument != null ? GetInstrumentFilterName(instrument) : GetInstrumentFilterName(fullName);
                if (!MatchesReplayFolderFilter(fullName, baseInstrument, selectedInstruments))
                    continue;

                if (instruments.Count == 0)
                    logout(string.Format("WARNING: Unable to find an instrument named \"{0}\". Audit can report file existence but cannot read timestamps.", fullName));
                else if (instruments.Count > 1)
                    logout(string.Format("WARNING: More than one instrument identified for name \"{0}\". Audit can report file existence but cannot read timestamps.", fullName));

                folders.Add(new ReplayContractFolder()
                {
                    DirectoryPath = instrumentDir,
                    ContractFolder = fullName,
                    BaseInstrument = baseInstrument,
                    Instrument = instrument,
                });
            }

            return folders
                .OrderBy(folder => folder.BaseInstrument, StringComparer.OrdinalIgnoreCase)
                .ThenBy(folder => folder.ContractFolder, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<ReplayAuditEntry> BuildReplayAuditEntries(List<ReplayContractFolder> folders, DateTime? selectedStartDate, DateTime? selectedEndDate, HashSet<string> selectedInstruments)
        {
            List<ReplayAuditEntry> entries = new List<ReplayAuditEntry>();
            foreach (ReplayContractFolder folder in folders)
            {
                string[] fileEntries = Directory.GetFiles(folder.DirectoryPath, "*.nrd");
                foreach (string fileName in fileEntries)
                {
                    DateTime? fileDate = TryParseReplayFileDate(fileName);
                    if (fileDate.HasValue)
                    {
                        if (selectedStartDate.HasValue && fileDate.Value.Date < selectedStartDate.Value.Date)
                            continue;
                        if (selectedEndDate.HasValue && fileDate.Value.Date > selectedEndDate.Value.Date)
                            continue;
                    }
                    else if (selectedStartDate.HasValue || selectedEndDate.HasValue)
                    {
                        continue;
                    }

                    FileInfo fileInfo = new FileInfo(fileName);
                    entries.Add(new ReplayAuditEntry()
                    {
                        ContractFolder = folder.ContractFolder,
                        BaseInstrument = folder.BaseInstrument,
                        Instrument = folder.Instrument,
                        Date = fileDate,
                        FilePath = fileName,
                        FileName = Path.GetFileName(fileName),
                        FileExists = true,
                        FileBytes = fileInfo.Exists ? fileInfo.Length : 0,
                    });
                }
            }

            AddMissingReplayAuditEntries(entries, folders, selectedStartDate, selectedEndDate, selectedInstruments);
            MarkReplayAuditDuplicates(entries);

            return entries
                .OrderBy(entry => entry.Date.HasValue ? entry.Date.Value : DateTime.MaxValue)
                .ThenBy(entry => entry.BaseInstrument, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.ContractFolder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void AddMissingReplayAuditEntries(List<ReplayAuditEntry> entries, List<ReplayContractFolder> folders, DateTime? selectedStartDate, DateTime? selectedEndDate, HashSet<string> selectedInstruments)
        {
            DateTime auditStartDate;
            DateTime auditEndDate;
            if (!TryResolveReplayAuditDateRange(entries, selectedStartDate, selectedEndDate, out auditStartDate, out auditEndDate))
                return;

            bool missingByContract = ShouldReportMissingByContract(folders, selectedInstruments);
            if (missingByContract)
            {
                HashSet<string> presentContractDates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ReplayAuditEntry entry in entries.Where(entry => entry.FileExists && entry.Date.HasValue))
                    presentContractDates.Add(BuildReplayAuditKey(entry.ContractFolder, entry.Date.Value));

                foreach (ReplayContractFolder folder in folders)
                {
                    for (DateTime date = auditStartDate.Date; date <= auditEndDate.Date; date = date.AddDays(1))
                    {
                        if (!IsExpectedReplayCoverageDate(date))
                            continue;
                        if (presentContractDates.Contains(BuildReplayAuditKey(folder.ContractFolder, date)))
                            continue;

                        entries.Add(CreateMissingReplayAuditEntry(folder.ContractFolder, folder.BaseInstrument, folder.Instrument, date,
                            Path.Combine(folder.DirectoryPath, date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".nrd")));
                    }
                }

                return;
            }

            HashSet<string> presentBaseDates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ReplayAuditEntry entry in entries.Where(entry => entry.FileExists && entry.Date.HasValue))
                presentBaseDates.Add(BuildReplayAuditKey(entry.BaseInstrument, entry.Date.Value));

            foreach (string baseInstrument in folders
                .Select(folder => folder.BaseInstrument)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                for (DateTime date = auditStartDate.Date; date <= auditEndDate.Date; date = date.AddDays(1))
                {
                    if (!IsExpectedReplayCoverageDate(date))
                        continue;
                    if (presentBaseDates.Contains(BuildReplayAuditKey(baseInstrument, date)))
                        continue;

                    entries.Add(CreateMissingReplayAuditEntry(baseInstrument, baseInstrument, null, date,
                        date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".nrd"));
                }
            }
        }

        private ReplayAuditEntry CreateMissingReplayAuditEntry(string contractFolder, string baseInstrument, Cbi.Instrument instrument, DateTime date, string filePath)
        {
            return new ReplayAuditEntry()
            {
                ContractFolder = contractFolder,
                BaseInstrument = baseInstrument,
                Instrument = instrument,
                Date = date.Date,
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                FileExists = false,
                Status = ReplayAuditStatus.MISSING,
                Notes = "Expected Sunday-Friday replay coverage was not found.",
            };
        }

        private bool TryResolveReplayAuditDateRange(List<ReplayAuditEntry> entries, DateTime? selectedStartDate, DateTime? selectedEndDate, out DateTime auditStartDate, out DateTime auditEndDate)
        {
            DateTime? minFileDate = null;
            DateTime? maxFileDate = null;
            foreach (ReplayAuditEntry entry in entries.Where(entry => entry.Date.HasValue))
            {
                DateTime date = entry.Date.Value.Date;
                if (!minFileDate.HasValue || date < minFileDate.Value)
                    minFileDate = date;
                if (!maxFileDate.HasValue || date > maxFileDate.Value)
                    maxFileDate = date;
            }

            if (selectedStartDate.HasValue)
                auditStartDate = selectedStartDate.Value.Date;
            else if (minFileDate.HasValue)
                auditStartDate = minFileDate.Value.Date;
            else if (selectedEndDate.HasValue)
                auditStartDate = selectedEndDate.Value.Date;
            else
            {
                auditStartDate = DateTime.MinValue;
                auditEndDate = DateTime.MinValue;
                return false;
            }

            if (selectedEndDate.HasValue)
                auditEndDate = selectedEndDate.Value.Date;
            else if (maxFileDate.HasValue)
                auditEndDate = maxFileDate.Value.Date;
            else
                auditEndDate = auditStartDate;

            return auditStartDate <= auditEndDate;
        }

        private bool ShouldReportMissingByContract(List<ReplayContractFolder> folders, HashSet<string> selectedInstruments)
        {
            if (selectedInstruments == null || selectedInstruments.Count == 0)
                return false;

            foreach (string selectedInstrument in selectedInstruments)
            {
                bool matchesContractFolder = folders.Any(folder => string.Equals(folder.ContractFolder, selectedInstrument, StringComparison.OrdinalIgnoreCase));
                if (!matchesContractFolder)
                    return false;
            }

            return true;
        }

        private void MarkReplayAuditDuplicates(List<ReplayAuditEntry> entries)
        {
            foreach (IGrouping<string, ReplayAuditEntry> group in entries
                .Where(entry => entry.FileExists && entry.Date.HasValue)
                .GroupBy(entry => BuildReplayAuditKey(entry.BaseInstrument, entry.Date.Value), StringComparer.OrdinalIgnoreCase))
            {
                List<ReplayAuditEntry> duplicateEntries = group.ToList();
                if (duplicateEntries.Count <= 1)
                    continue;

                string duplicateFolders = string.Join(", ", duplicateEntries
                    .Select(entry => entry.ContractFolder)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                foreach (ReplayAuditEntry entry in duplicateEntries)
                {
                    entry.IsDuplicate = true;
                    entry.DuplicateContractFolders = duplicateFolders;
                }
            }
        }

        private void RunReplayAudit(string nrdRoot, string outputDir, List<ReplayAuditEntry> entries, DateTime? selectedStartDate, DateTime? selectedEndDate, HashSet<string> selectedInstruments)
        {
            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (canceling)
                        break;

                    ReplayAuditEntry entry = entries[i];
                    if (entry.FileExists)
                        ScanReplayAuditFile(entry);
                    FinalizeReplayAuditStatus(entry);
                    logout(FormatReplayAuditLogLine(entry));

                    Dispatcher.InvokeAsync(() =>
                    {
                        pbProgress.Value++;
                        UpdateProgressLabel(entries.Count);
                    });
                }

                if (canceling)
                {
                    logout("Replay coverage audit canceled");
                    return;
                }

                ReplayAuditReportPaths reportPaths = WriteReplayAuditReports(nrdRoot, outputDir, entries, selectedStartDate, selectedEndDate, selectedInstruments);
                logout(string.Format("Replay coverage audit complete. CSV: {0}", reportPaths.CsvPath));
                logout(string.Format("Replay coverage audit summary: {0}", reportPaths.TxtPath));
            }
            catch (Exception error)
            {
                logout(string.Format("ERROR: Replay coverage audit failed: {0}", error));
            }
            finally
            {
                complete();
            }
        }

        private void ScanReplayAuditFile(ReplayAuditEntry entry)
        {
            if (!entry.Date.HasValue)
            {
                AppendReplayAuditNote(entry, "File name is not a yyyyMMdd replay date.");
                return;
            }
            if (entry.Instrument == null)
            {
                AppendReplayAuditNote(entry, "Unable to resolve the NT8 instrument for this replay folder.");
                return;
            }

            string temporaryCsvFileName = Path.Combine(Path.GetTempPath(),
                "NRDToCSV-Audit-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                MarketReplay.DumpMarketDepth(entry.Instrument, entry.Date.Value, entry.Date.Value, temporaryCsvFileName);
                ReadReplayAuditDump(entry, temporaryCsvFileName);
            }
            catch (Exception error)
            {
                AppendReplayAuditNote(entry, "Unable to read replay timestamps: " + error.Message);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryCsvFileName))
                        File.Delete(temporaryCsvFileName);
                }
                catch (Exception error)
                {
                    AppendReplayAuditNote(entry, "Unable to delete temporary audit CSV: " + error.Message);
                }
            }
        }

        private void ReadReplayAuditDump(ReplayAuditEntry entry, string temporaryCsvFileName)
        {
            TimeZoneInfo easternTimeZone = GetEasternTimeZone();
            using (StreamReader reader = new StreamReader(temporaryCsvFileName))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (canceling)
                        break;
                    if (!IsReplayLevel1AskBidOrTrade(line))
                        continue;

                    DateTimeOffset timestamp;
                    if (!TryParseReplayTimestamp(line, easternTimeZone, out timestamp))
                    {
                        entry.InvalidTimestampRows++;
                        continue;
                    }

                    entry.Level1RowCount++;
                    if (!entry.FirstTimestampEt.HasValue)
                        entry.FirstTimestampEt = timestamp;
                    entry.LastTimestampEt = timestamp;
                }
            }
        }

        private void FinalizeReplayAuditStatus(ReplayAuditEntry entry)
        {
            if (!entry.FileExists)
            {
                entry.Status = ReplayAuditStatus.MISSING;
                return;
            }

            ReplayAuditStatus coverageStatus = GetReplayAuditCoverageStatus(entry);
            if (entry.IsDuplicate)
            {
                AppendReplayAuditNote(entry, "Duplicate same-date " + entry.BaseInstrument + " replay files: " + entry.DuplicateContractFolders + ".");
                if (coverageStatus != ReplayAuditStatus.SUSPICIOUS)
                {
                    if (coverageStatus == ReplayAuditStatus.PARTIAL)
                        AppendReplayAuditNote(entry, "Coverage is also partial.");
                    entry.Status = ReplayAuditStatus.DUPLICATE;
                    return;
                }
            }

            entry.Status = coverageStatus;
        }

        private ReplayAuditStatus GetReplayAuditCoverageStatus(ReplayAuditEntry entry)
        {
            if (!entry.Date.HasValue)
            {
                AppendReplayAuditNote(entry, "File name is not a yyyyMMdd replay date.");
                return ReplayAuditStatus.SUSPICIOUS;
            }
            if (entry.FileBytes <= 0)
            {
                AppendReplayAuditNote(entry, "Replay file is empty.");
                return ReplayAuditStatus.SUSPICIOUS;
            }
            if (entry.Level1RowCount == 0 || !entry.FirstTimestampEt.HasValue || !entry.LastTimestampEt.HasValue)
            {
                AppendReplayAuditNote(entry, "No L1 ask/bid/trade timestamps were found.");
                return ReplayAuditStatus.SUSPICIOUS;
            }
            if (entry.InvalidTimestampRows > 0)
                AppendReplayAuditNote(entry, string.Format("{0} L1 row(s) had invalid timestamps.", entry.InvalidTimestampRows));
            if (entry.LastTimestampEt.Value < entry.FirstTimestampEt.Value)
            {
                AppendReplayAuditNote(entry, "Last timestamp is earlier than first timestamp.");
                return ReplayAuditStatus.SUSPICIOUS;
            }
            if (entry.Level1RowCount < 10)
            {
                AppendReplayAuditNote(entry, "Very low L1 row count.");
                return ReplayAuditStatus.SUSPICIOUS;
            }

            TimeZoneInfo easternTimeZone = GetEasternTimeZone();
            DateTime firstEt = TimeZoneInfo.ConvertTime(entry.FirstTimestampEt.Value, easternTimeZone).DateTime;
            DateTime lastEt = TimeZoneInfo.ConvertTime(entry.LastTimestampEt.Value, easternTimeZone).DateTime;
            DateTime expectedDate = entry.Date.Value.Date;
            if (firstEt.Date != expectedDate || lastEt.Date != expectedDate)
            {
                AppendReplayAuditNote(entry, string.Format("Timestamp date mismatch: starts {0:yyyy-MM-dd}, ends {1:yyyy-MM-dd}, file date {2:yyyy-MM-dd}.",
                    firstEt,
                    lastEt,
                    expectedDate));
                return ReplayAuditStatus.SUSPICIOUS;
            }

            switch (expectedDate.DayOfWeek)
            {
                case DayOfWeek.Saturday:
                    AppendReplayAuditNote(entry, "Saturday replay files are informational; Saturday is not required for expected coverage.");
                    return ReplayAuditStatus.OK;
                case DayOfWeek.Sunday:
                    if (firstEt.TimeOfDay < TimeSpan.FromHours(17))
                    {
                        AppendReplayAuditNote(entry, "Sunday replay starts before the expected Globex-open window.");
                        return ReplayAuditStatus.SUSPICIOUS;
                    }
                    if (firstEt.TimeOfDay > TimeSpan.FromMinutes(18 * 60 + 10) || lastEt.TimeOfDay < TimeSpan.FromMinutes(23 * 60 + 50))
                    {
                        AppendReplayAuditNote(entry, "Sunday coverage should normally start around Globex open and run to about 23:59 ET.");
                        return ReplayAuditStatus.PARTIAL;
                    }
                    return ReplayAuditStatus.OK;
                default:
                    if (firstEt.TimeOfDay > TimeSpan.FromMinutes(10) || lastEt.TimeOfDay < TimeSpan.FromMinutes(23 * 60 + 50))
                    {
                        AppendReplayAuditNote(entry, "Weekday coverage should normally run from about 00:00 ET to 23:59 ET.");
                        return ReplayAuditStatus.PARTIAL;
                    }
                    return ReplayAuditStatus.OK;
            }
        }

        private ReplayAuditReportPaths WriteReplayAuditReports(string nrdRoot, string outputDir, List<ReplayAuditEntry> entries, DateTime? selectedStartDate, DateTime? selectedEndDate, HashSet<string> selectedInstruments)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string csvPath = Path.Combine(outputDir, "replay-audit-" + timestamp + ".csv");
            string txtPath = Path.Combine(outputDir, "replay-audit-" + timestamp + ".txt");
            WriteReplayAuditCsv(csvPath, entries);
            WriteReplayAuditText(txtPath, nrdRoot, entries, selectedStartDate, selectedEndDate, selectedInstruments);
            return new ReplayAuditReportPaths()
            {
                CsvPath = csvPath,
                TxtPath = txtPath,
            };
        }

        private void WriteReplayAuditCsv(string csvPath, List<ReplayAuditEntry> entries)
        {
            using (StreamWriter writer = new StreamWriter(csvPath, false))
            {
                writer.WriteLine("ContractFolder,BaseInstrument,Date,FileName,FileExists,FirstTimestampET,LastTimestampET,Status,Level1RowCount,FileBytes,Notes");
                foreach (ReplayAuditEntry entry in entries)
                {
                    writer.WriteLine(string.Join(",",
                        Csv(entry.ContractFolder),
                        Csv(entry.BaseInstrument),
                        Csv(entry.Date.HasValue ? entry.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty),
                        Csv(entry.FileName),
                        Csv(entry.FileExists ? "true" : "false"),
                        Csv(FormatEtCsvTimestamp(entry.FirstTimestampEt)),
                        Csv(FormatEtCsvTimestamp(entry.LastTimestampEt)),
                        Csv(entry.Status.ToString()),
                        Csv(entry.Level1RowCount.ToString(CultureInfo.InvariantCulture)),
                        Csv(entry.FileBytes.ToString(CultureInfo.InvariantCulture)),
                        Csv(entry.Notes)));
                }
            }
        }

        private void WriteReplayAuditText(string txtPath, string nrdRoot, List<ReplayAuditEntry> entries, DateTime? selectedStartDate, DateTime? selectedEndDate, HashSet<string> selectedInstruments)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("NRD replay coverage audit");
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            builder.AppendLine("Replay root: " + nrdRoot);
            builder.AppendLine("Date range: " + FormatDateRange(selectedStartDate, selectedEndDate));
            builder.AppendLine("Instrument filter: " + FormatInstrumentFilter(selectedInstruments));
            builder.AppendLine();
            foreach (ReplayAuditStatus status in Enum.GetValues(typeof(ReplayAuditStatus)).Cast<ReplayAuditStatus>())
                builder.AppendLine(string.Format("{0}: {1}", status, entries.Count(entry => entry.Status == status)));
            builder.AppendLine();
            builder.AppendLine("Rows:");
            foreach (ReplayAuditEntry entry in entries)
                builder.AppendLine(FormatReplayAuditLogLine(entry));

            File.WriteAllText(txtPath, builder.ToString());
        }

        private string FormatReplayAuditLogLine(ReplayAuditEntry entry)
        {
            string dateText = entry.Date.HasValue ? entry.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "(no-date)";
            string contractText = string.IsNullOrWhiteSpace(entry.ContractFolder) ? "(unknown)" : entry.ContractFolder;
            string line = string.Format("{0} {1} {2}", dateText, contractText, entry.Status);
            if (!entry.FileExists)
                line += " no replay file";
            else if (entry.FirstTimestampEt.HasValue || entry.LastTimestampEt.HasValue)
                line += string.Format(" starts {0}, ends {1}, rows {2}",
                    FormatEtLogTime(entry.FirstTimestampEt),
                    FormatEtLogTime(entry.LastTimestampEt),
                    entry.Level1RowCount.ToString(CultureInfo.InvariantCulture));
            else
                line += " no L1 timestamps";

            if (!string.IsNullOrWhiteSpace(entry.Notes))
                line += " (" + entry.Notes + ")";
            return line;
        }

        private static string Csv(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }

        private static string FormatDateRange(DateTime? selectedStartDate, DateTime? selectedEndDate)
        {
            if (selectedStartDate.HasValue && selectedEndDate.HasValue)
                return string.Format("{0:yyyy-MM-dd} to {1:yyyy-MM-dd}", selectedStartDate.Value, selectedEndDate.Value);
            if (selectedStartDate.HasValue)
                return string.Format("{0:yyyy-MM-dd} to existing max date", selectedStartDate.Value);
            if (selectedEndDate.HasValue)
                return string.Format("existing min date to {0:yyyy-MM-dd}", selectedEndDate.Value);
            return "existing file date range";
        }

        private static string FormatInstrumentFilter(HashSet<string> selectedInstruments)
        {
            if (selectedInstruments == null || selectedInstruments.Count == 0)
                return "(all)";
            return string.Join(", ", selectedInstruments.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }

        private static string FormatEtLogTime(DateTimeOffset? timestamp)
        {
            if (!timestamp.HasValue)
                return "n/a";
            DateTime et = TimeZoneInfo.ConvertTime(timestamp.Value, GetEasternTimeZone()).DateTime;
            return et.ToString("HH:mm", CultureInfo.InvariantCulture) + " ET";
        }

        private static string FormatEtCsvTimestamp(DateTimeOffset? timestamp)
        {
            if (!timestamp.HasValue)
                return string.Empty;
            DateTime et = TimeZoneInfo.ConvertTime(timestamp.Value, GetEasternTimeZone()).DateTime;
            return et.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + " ET";
        }

        private static void AppendReplayAuditNote(ReplayAuditEntry entry, string note)
        {
            if (entry == null || string.IsNullOrWhiteSpace(note))
                return;
            if (string.IsNullOrWhiteSpace(entry.Notes))
                entry.Notes = note;
            else if (entry.Notes.IndexOf(note, StringComparison.OrdinalIgnoreCase) < 0)
                entry.Notes += " " + note;
        }

        private static bool IsExpectedReplayCoverageDate(DateTime date)
        {
            return date.DayOfWeek != DayOfWeek.Saturday;
        }

        private static string BuildReplayAuditKey(string name, DateTime date)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1:yyyyMMdd}", name ?? string.Empty, date.Date);
        }

        private static DateTime? TryParseReplayFileDate(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            DateTime date;
            if (name != null
                && name.Length == 8
                && DateTime.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return date.Date;
            }

            return null;
        }

        private void StartHistoricalExport(string csvDir, IEnumerable<string> instrumentDirs, DateTime selectedStartDate, DateTime selectedEndDate, HashSet<string> selectedInstruments)
        {
            List<HistoricalDumpEntry> entries = BuildHistoricalEntries(csvDir, instrumentDirs, selectedStartDate, selectedEndDate, selectedInstruments);
            if (entries.Count == 0)
            {
                if (selectedInstruments.Count > 0)
                    logout(string.Format("No historical data export jobs found for instrument filter \"{0}\"", cbInstrumentFilter.Text));
                else
                    logout("No historical data found to export");
                return;
            }

            logout(string.Format("Export {0} historical files...", entries.Count));
            run(entries.Count, false);
            RunHistoricalConversion(entries, 0);
        }

        private void ProceedDirectory(List<DumpEntry> entries, string nrdRoot, string nrdDir, string csvDir, DateTime? selectedStartDate, DateTime? selectedEndDate, HashSet<string> selectedInstruments)
        {
            string[] fileEntries = Directory.GetFiles(nrdDir, "*.nrd");
            if (fileEntries.Length == 0)
            {
                logout(string.Format("WARNING: No *.nrd files found in \"{0}\" directory. Skipped", nrdDir));
                return;
            }

            foreach (string fileName in fileEntries)
            {
                string fullName = Path.GetFileName(Path.GetDirectoryName(fileName));
                string relativeName = fileName.Substring(nrdRoot.Length);

                Collection<Instrument> instruments = InstrumentList.GetInstruments(fullName);
                if (instruments.Count == 0)
                {
                    logout(string.Format("Unable to find an instrument named \"{0}\". Skipped", fullName));
                    continue;
                }
                else if (instruments.Count > 1)
                {
                    logout(string.Format("More than one instrument identified for name \"{0}\". Skipped", fullName));
                    continue;
                }
                Cbi.Instrument instrument = instruments[0];
                if (!MatchesInstrumentFilter(instrument, selectedInstruments))
                    continue;
                string name = Path.GetFileNameWithoutExtension(fileName);
                DateTime sourceDate = new DateTime(
                    Convert.ToInt16(name.Substring(0, 4)),
                    Convert.ToInt16(name.Substring(4, 2)),
                    Convert.ToInt16(name.Substring(6, 2)));
                DateTime replayDate = sourceDate;
                if (selectedStartDate.HasValue && replayDate.Date < selectedStartDate.Value.Date)
                    continue;
                if (selectedEndDate.HasValue && replayDate.Date > selectedEndDate.Value.Date)
                    continue;

                string csvFileName = string.Format("{0}.csv", Path.Combine(csvDir, instrument.FullName, replayDate.ToString("yyyyMMdd")));
                if (File.Exists(csvFileName))
                {
                    logout(string.Format("Conversion \"{0}\" to \"{1}\" is done already. Skipped",
                        relativeName.Substring(1), csvFileName.Substring(csvDir.Length + 1)));
                    continue;
                }
                long nrdFileLength = new FileInfo(fileName).Length;
                totalFilesLength += nrdFileLength;
                entries.Add(new DumpEntry()
                {
                    NrdLength = nrdFileLength,
                    Instrument = instrument,
                    Date = sourceDate,
                    CsvFileName = csvFileName,
                    FromName = relativeName.Substring(1),
                    ToName = csvFileName.Substring(csvDir.Length + 1),
                });
            }
        }

        private void RunReplayConversion(List<DumpEntry> entries, int offset, int increment)
        {
            Globals.RandomDispatcher.InvokeAsync(new Action(() =>
            {
                for (int i = offset; i < entries.Count; i += increment)
                {
                    ConvertReplayNrd(entries[i]);
                    Dispatcher.InvokeAsync(() =>
                    {
                        pbProgress.Value++;
                        completeFilesLength += entries[i].NrdLength;
                        UpdateProgressLabel(entries.Count);
                    });
                    if (canceling) break;
                }
                if (--taskCount == 0)
                {
                    complete();
                    if (canceling)
                    {
                        logout("Conversion canceled");
                    }
                    else
                    {
                        logout("Conversion complete");
                    }
                }
            }));
        }

        private List<HistoricalDumpEntry> BuildHistoricalEntries(string csvDir, IEnumerable<string> instrumentDirs, DateTime selectedStartDate, DateTime selectedEndDate, HashSet<string> selectedInstruments)
        {
            HistoricalSeriesKind seriesKind = GetSelectedHistoricalSeriesKind();
            string seriesName = GetHistoricalSeriesLabel(seriesKind);
            List<HistoricalDumpEntry> entries = new List<HistoricalDumpEntry>();

            foreach (string instrumentDir in instrumentDirs)
            {
                string fullName = Path.GetFileName(instrumentDir);
                if (string.IsNullOrWhiteSpace(fullName))
                    continue;

                Collection<Instrument> instruments = InstrumentList.GetInstruments(fullName);
                if (instruments.Count == 0)
                {
                    logout(string.Format("Unable to find an instrument named \"{0}\". Skipped", fullName));
                    continue;
                }
                if (instruments.Count > 1)
                {
                    logout(string.Format("More than one instrument identified for name \"{0}\". Skipped", fullName));
                    continue;
                }

                Cbi.Instrument instrument = instruments[0];
                if (!MatchesInstrumentFilter(instrument, selectedInstruments))
                    continue;

                string relativeCsvName = Path.Combine("historical", seriesName, instrument.FullName,
                    string.Format("{0}_{1}.csv", selectedStartDate.ToString("yyyyMMdd"), selectedEndDate.ToString("yyyyMMdd")));
                string csvFileName = Path.Combine(csvDir, relativeCsvName);
                if (File.Exists(csvFileName))
                {
                    logout(string.Format("Conversion \"{0}\" to \"{1}\" is done already. Skipped",
                        string.Format("{0} {1:yyyy-MM-dd} to {2:yyyy-MM-dd}", instrument.FullName, selectedStartDate, selectedEndDate),
                        relativeCsvName));
                    continue;
                }

                entries.Add(new HistoricalDumpEntry()
                {
                    Instrument = instrument,
                    SeriesKind = seriesKind,
                    FromLocal = selectedStartDate,
                    ToLocal = selectedEndDate,
                    CsvFileName = csvFileName,
                    FromName = string.Format("{0} {1} {2:yyyy-MM-dd} to {3:yyyy-MM-dd}", seriesName, instrument.FullName, selectedStartDate, selectedEndDate),
                    ToName = relativeCsvName,
                });
            }

            return entries;
        }

        private string GetHistoricalSeriesLabel(HistoricalSeriesKind seriesKind)
        {
            switch (seriesKind)
            {
                case HistoricalSeriesKind.Day:
                    return "Day";
                case HistoricalSeriesKind.Minute:
                    return "Minute";
                default:
                    return "Tick";
            }
        }

        private BarsPeriodType GetBarsPeriodType(HistoricalSeriesKind seriesKind)
        {
            switch (seriesKind)
            {
                case HistoricalSeriesKind.Day:
                    return BarsPeriodType.Day;
                case HistoricalSeriesKind.Minute:
                    return BarsPeriodType.Minute;
                default:
                    return BarsPeriodType.Tick;
            }
        }

        private void RunHistoricalConversion(List<HistoricalDumpEntry> entries, int index)
        {
            if (canceling || index >= entries.Count)
            {
                complete();
                logout(canceling ? "Conversion canceled" : "Conversion complete");
                return;
            }

            HistoricalDumpEntry entry = entries[index];
            logout(string.Format("Conversion \"{0}\" to \"{1}\"...", entry.FromName, entry.ToName));

            string csvFileDir = Path.GetDirectoryName(entry.CsvFileName);
            if (!Directory.Exists(csvFileDir))
            {
                try
                {
                    Directory.CreateDirectory(csvFileDir);
                }
                catch (Exception error)
                {
                    logout(string.Format("ERROR: Unable to create the CSV file directory \"{0}\": {1}",
                        csvFileDir, error));
                    Dispatcher.InvokeAsync(() =>
                    {
                        pbProgress.Value++;
                        UpdateProgressLabel(entries.Count);
                    });
                    RunHistoricalConversion(entries, index + 1);
                    return;
                }
            }

            BarsRequest barsRequest = new BarsRequest(entry.Instrument, entry.FromLocal, entry.ToLocal);
            barsRequest.BarsPeriod = new BarsPeriod
            {
                BarsPeriodType = GetBarsPeriodType(entry.SeriesKind),
                Value = 1,
                MarketDataType = MarketDataType.Last,
            };
            barsRequest.TradingHours = TradingHours.Get("Default 24 x 7");
            barsRequest.LookupPolicy = LookupPolicies.Repository;
            barsRequest.MergePolicy = MergePolicy.DoNotMerge;
            barsRequest.Request((request, errorCode, errorMessage) =>
            {
                try
                {
                    if (errorCode != ErrorCode.NoError)
                    {
                        logout(string.Format("ERROR: Historical export failed for \"{0}\": {1} {2}", entry.FromName, errorCode, errorMessage));
                    }
                    else
                    {
                        int writtenRows = WriteHistoricalCsv(entry, request);
                        if (writtenRows < 0)
                        {
                            logout(string.Format("Conversion \"{0}\" canceled before completion", entry.FromName));
                        }
                        else if (writtenRows == 0)
                        {
                            if (File.Exists(entry.CsvFileName))
                                File.Delete(entry.CsvFileName);
                            logout(string.Format("No historical data returned for \"{0}\". Skipped", entry.FromName));
                        }
                        else
                        {
                            logout(string.Format("Conversion \"{0}\" to \"{1}\" complete ({2} rows)", entry.FromName, entry.ToName, writtenRows));
                        }
                    }
                }
                catch (Exception error)
                {
                    logout(string.Format("ERROR: Historical export failed for \"{0}\": {1}", entry.FromName, error));
                }
                finally
                {
                    request.Dispose();
                    Dispatcher.InvokeAsync(() =>
                    {
                        pbProgress.Value++;
                        UpdateProgressLabel(entries.Count);
                    });
                    RunHistoricalConversion(entries, index + 1);
                }
            });
        }

        private int WriteHistoricalCsv(HistoricalDumpEntry entry, BarsRequest request)
        {
            int writtenRows = 0;
            bool canceledDuringWrite = false;
            using (StreamWriter writer = new StreamWriter(entry.CsvFileName, false))
            {
                for (int i = 0; i < request.Bars.Count; i++)
                {
                    if (canceling)
                    {
                        canceledDuringWrite = true;
                        break;
                    }

                    writer.WriteLine(FormatHistoricalCsvLine(entry.SeriesKind, request, i));
                    writtenRows++;
                }
            }

            if (canceledDuringWrite)
            {
                if (File.Exists(entry.CsvFileName))
                    File.Delete(entry.CsvFileName);
                return -1;
            }

            return writtenRows;
        }

        private string FormatHistoricalCsvLine(HistoricalSeriesKind seriesKind, BarsRequest request, int index)
        {
            DateTime time = request.Bars.GetTime(index);
            switch (seriesKind)
            {
                case HistoricalSeriesKind.Day:
                    return string.Format(CultureInfo.InvariantCulture, "{0};{1};{2};{3};{4};{5}",
                        time.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                        request.Bars.GetOpen(index),
                        request.Bars.GetHigh(index),
                        request.Bars.GetLow(index),
                        request.Bars.GetClose(index),
                        request.Bars.GetVolume(index));
                case HistoricalSeriesKind.Minute:
                    return string.Format(CultureInfo.InvariantCulture, "{0};{1};{2};{3};{4};{5}",
                        time.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture),
                        request.Bars.GetOpen(index),
                        request.Bars.GetHigh(index),
                        request.Bars.GetLow(index),
                        request.Bars.GetClose(index),
                        request.Bars.GetVolume(index));
                default:
                    return string.Format(CultureInfo.InvariantCulture, "{0};{1};{2}",
                        time.ToString("yyyyMMdd HHmmss fffffff", CultureInfo.InvariantCulture),
                        request.Bars.GetClose(index),
                        request.Bars.GetVolume(index));
            }
        }

        private void RefreshInstrumentFilterOptions(IEnumerable<string> instrumentDirs = null)
        {
            if (cbInstrumentFilter == null)
                return;

            string selectedText = cbInstrumentFilter.Text;
            IEnumerable<string> directories = instrumentDirs;
            if (directories == null)
            {
                string dataRootDir = GetDataRootDirectory(GetSelectedExportMode());
                directories = Directory.Exists(dataRootDir)
                    ? Directory.GetDirectories(dataRootDir)
                    : Enumerable.Empty<string>();
            }

            List<string> availableInstruments = new List<string>();
            foreach (string subDir in directories)
            {
                string fullName = Path.GetFileName(subDir);
                if (string.IsNullOrWhiteSpace(fullName))
                    continue;

                Collection<Instrument> instruments = InstrumentList.GetInstruments(fullName);
                string instrumentFilterName = instruments.Count == 1
                    ? GetInstrumentFilterName(instruments[0])
                    : GetInstrumentFilterName(fullName);
                if (!string.IsNullOrWhiteSpace(instrumentFilterName))
                    availableInstruments.Add(instrumentFilterName);
            }

            cbInstrumentFilter.ItemsSource = availableInstruments
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            cbInstrumentFilter.Text = selectedText ?? string.Empty;
        }

        private HashSet<string> ParseInstrumentFilter()
        {
            if (cbInstrumentFilter == null || string.IsNullOrWhiteSpace(cbInstrumentFilter.Text))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return new HashSet<string>(
                cbInstrumentFilter.Text
                    .Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        private bool MatchesInstrumentFilter(Cbi.Instrument instrument, HashSet<string> selectedInstruments)
        {
            if (selectedInstruments == null || selectedInstruments.Count == 0)
                return true;

            string instrumentFilterName = GetInstrumentFilterName(instrument);
            string fullName = instrument != null ? (instrument.FullName ?? string.Empty).Trim() : string.Empty;
            return (!string.IsNullOrWhiteSpace(instrumentFilterName) && selectedInstruments.Contains(instrumentFilterName))
                || (!string.IsNullOrWhiteSpace(fullName) && selectedInstruments.Contains(fullName));
        }

        private bool MatchesReplayFolderFilter(string fullName, string instrumentFilterName, HashSet<string> selectedInstruments)
        {
            if (selectedInstruments == null || selectedInstruments.Count == 0)
                return true;

            return (!string.IsNullOrWhiteSpace(instrumentFilterName) && selectedInstruments.Contains(instrumentFilterName))
                || (!string.IsNullOrWhiteSpace(fullName) && selectedInstruments.Contains(fullName));
        }

        private string GetInstrumentFilterName(Cbi.Instrument instrument)
        {
            if (instrument == null)
                return string.Empty;

            if (instrument.MasterInstrument != null && !string.IsNullOrWhiteSpace(instrument.MasterInstrument.Name))
                return instrument.MasterInstrument.Name.Trim();

            return !string.IsNullOrWhiteSpace(instrument.FullName) ? instrument.FullName.Trim() : string.Empty;
        }

        private string GetInstrumentFilterName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return string.Empty;

            string trimmed = fullName.Trim();
            int separatorIndex = trimmed.IndexOf(' ');
            if (separatorIndex > 0)
                return trimmed.Substring(0, separatorIndex).Trim();

            return trimmed;
        }

        private void ConvertReplayNrd(DumpEntry entry)
        {
            logout(string.Format("Conversion \"{0}\" to \"{1}\"...", entry.FromName, entry.ToName));

            string csvFileDir = Path.GetDirectoryName(entry.CsvFileName);
            if (!Directory.Exists(csvFileDir))
            {
                try
                {
                    Directory.CreateDirectory(csvFileDir);
                }
                catch (Exception error)
                {
                    logout(string.Format("ERROR: Unable to create the CSV file directory \"{0}\": {1}",
                        csvFileDir, error.ToString()));
                }
            }

            string temporaryCsvFileName = entry.CsvFileName + ".full.tmp";
            try
            {
                if (File.Exists(temporaryCsvFileName))
                    File.Delete(temporaryCsvFileName);

                MarketReplay.DumpMarketDepth(entry.Instrument, entry.Date, entry.Date, temporaryCsvFileName);
                ReplayCsvWriteResult writeResult = WriteReplayLevel1Csv(temporaryCsvFileName, entry.CsvFileName);
                int writtenRows = writeResult.WrittenRows;

                if (writtenRows < 0)
                {
                    logout(string.Format("Conversion \"{0}\" canceled before completion", entry.FromName));
                    return;
                }

                WarnIfReplayDateMismatch(entry, writeResult);
                logout(string.Format("Filtered \"{0}\" to L1 ask/bid/trade rows ({1} rows)", entry.ToName, writtenRows));
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryCsvFileName))
                        File.Delete(temporaryCsvFileName);
                }
                catch (Exception error)
                {
                    logout(string.Format("WARNING: Unable to delete temporary replay CSV \"{0}\": {1}",
                        temporaryCsvFileName, error));
                }
            }

            logout(string.Format("Conversion \"{0}\" to \"{1}\" complete", entry.FromName, entry.ToName));
        }

        private void WarnIfReplayDateMismatch(DumpEntry entry, ReplayCsvWriteResult writeResult)
        {
            if (writeResult.FirstTimestampDate.HasValue && writeResult.FirstTimestampDate.Value.Date != entry.Date.Date)
            {
                logout(string.Format(
                    "WARNING: First CSV timestamp date {0:yyyy-MM-dd} does not match target replay date {1:yyyy-MM-dd} for \"{2}\"",
                    writeResult.FirstTimestampDate.Value,
                    entry.Date,
                    entry.ToName));
            }

            if (writeResult.LastTimestampDate.HasValue && writeResult.LastTimestampDate.Value.Date != entry.Date.Date)
            {
                logout(string.Format(
                    "WARNING: Last CSV timestamp date {0:yyyy-MM-dd} does not match target replay date {1:yyyy-MM-dd} for \"{2}\"",
                    writeResult.LastTimestampDate.Value,
                    entry.Date,
                    entry.ToName));
            }
        }

        private ReplayCsvWriteResult WriteReplayLevel1Csv(string sourceCsvFileName, string targetCsvFileName)
        {
            int writtenRows = 0;
            bool canceledDuringWrite = false;
            DateTime? firstTimestampDate = null;
            DateTime? lastTimestampDate = null;

            using (StreamReader reader = new StreamReader(sourceCsvFileName))
            using (StreamWriter writer = new StreamWriter(targetCsvFileName, false))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (canceling)
                    {
                        canceledDuringWrite = true;
                        break;
                    }

                    if (!IsReplayLevel1AskBidOrTrade(line))
                        continue;

                    writer.WriteLine(line);
                    writtenRows++;

                    DateTime? timestampDate = TryParseReplayTimestampDate(line);
                    if (timestampDate.HasValue)
                    {
                        if (!firstTimestampDate.HasValue)
                            firstTimestampDate = timestampDate.Value;
                        lastTimestampDate = timestampDate.Value;
                    }
                }
            }

            if (canceledDuringWrite)
            {
                if (File.Exists(targetCsvFileName))
                    File.Delete(targetCsvFileName);
                return new ReplayCsvWriteResult()
                {
                    WrittenRows = -1,
                };
            }

            return new ReplayCsvWriteResult()
            {
                WrittenRows = writtenRows,
                FirstTimestampDate = firstTimestampDate,
                LastTimestampDate = lastTimestampDate,
            };
        }

        private static DateTime? TryParseReplayTimestampDate(string line)
        {
            string[] parts = line.Split(';');
            if (parts.Length < 3 || parts[2].Length < 8)
                return null;

            DateTime timestampDate;
            if (DateTime.TryParseExact(
                parts[2].Substring(0, 8),
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out timestampDate))
            {
                return timestampDate.Date;
            }

            return null;
        }

        private static bool TryParseReplayTimestamp(string line, TimeZoneInfo replayTimeZone, out DateTimeOffset timestamp)
        {
            timestamp = DateTimeOffset.MinValue;
            string[] parts = line.Split(';');
            if (parts.Length < 4)
                return false;

            DateTime baseLocal;
            if (!DateTime.TryParseExact(parts[2], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out baseLocal))
                return false;

            long offsetTicks;
            if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out offsetTicks))
                return false;

            timestamp = new DateTimeOffset(baseLocal, replayTimeZone.GetUtcOffset(baseLocal)).AddTicks(offsetTicks);
            return true;
        }

        private static TimeZoneInfo GetEasternTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                }
                catch
                {
                    return TimeZoneInfo.Local;
                }
            }
        }

        private static bool IsReplayLevel1AskBidOrTrade(string line)
        {
            return line.StartsWith("L1;0;", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("L1;1;", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("L1;2;", StringComparison.OrdinalIgnoreCase);
        }

        public void Restore(XDocument document, XElement element)
        {
            foreach (XElement elRoot in element.Elements())
            {
                if (elRoot.Name.LocalName.Contains("NRDToCSV"))
                {
                    XElement elCsvRootDir = elRoot.Element("CsvRootDir");
                    if (elCsvRootDir != null)
                        tbCsvRootDir.Text = elCsvRootDir.Value;

                    XElement elExportMode = elRoot.Element("ExportMode");
                    if (elExportMode != null)
                        SelectComboValue(cbExportMode, ParseEnumValue(elExportMode.Value, ExportMode.Replay));

                    XElement elHistoricalSeries = elRoot.Element("HistoricalSeries");
                    if (elHistoricalSeries != null)
                        SelectComboValue(cbHistoricalSeries, ParseEnumValue(elHistoricalSeries.Value, HistoricalSeriesKind.Tick));

                    XElement elStartDate = elRoot.Element("StartDate");
                    if (elStartDate != null)
                    {
                        DateTime parsed;
                        if (DateTime.TryParse(elStartDate.Value, out parsed))
                            dpStartDate.SelectedDate = parsed.Date;
                    }

                    XElement elEndDate = elRoot.Element("EndDate");
                    if (elEndDate != null)
                    {
                        DateTime parsed;
                        if (DateTime.TryParse(elEndDate.Value, out parsed))
                            dpEndDate.SelectedDate = parsed.Date;
                    }

                    XElement elInstrumentFilter = elRoot.Element("InstrumentFilter");
                    if (elInstrumentFilter != null && cbInstrumentFilter != null)
                        cbInstrumentFilter.Text = elInstrumentFilter.Value;

                    UpdateModeUiState();
                }
            }
        }

        public void Save(XDocument document, XElement element)
        {
            element.Elements().Where(el => el.Name.LocalName.Equals("NRDToCSV")).Remove();
            XElement elRoot = new XElement("NRDToCSV");
            XElement elCsvRootDir = new XElement("CsvRootDir", tbCsvRootDir.Text);
            XElement elExportMode = new XElement("ExportMode", GetSelectedExportMode().ToString());
            XElement elHistoricalSeries = new XElement("HistoricalSeries", GetSelectedHistoricalSeriesKind().ToString());
            XElement elStartDate = new XElement("StartDate", dpStartDate.SelectedDate.HasValue ? dpStartDate.SelectedDate.Value.ToString("yyyy-MM-dd") : string.Empty);
            XElement elEndDate = new XElement("EndDate", dpEndDate.SelectedDate.HasValue ? dpEndDate.SelectedDate.Value.ToString("yyyy-MM-dd") : string.Empty);
            XElement elInstrumentFilter = new XElement("InstrumentFilter", cbInstrumentFilter != null ? cbInstrumentFilter.Text : string.Empty);
            elRoot.Add(elCsvRootDir);
            elRoot.Add(elExportMode);
            elRoot.Add(elHistoricalSeries);
            elRoot.Add(elStartDate);
            elRoot.Add(elEndDate);
            elRoot.Add(elInstrumentFilter);
            element.Add(elRoot);
        }

        public WorkspaceOptions WorkspaceOptions { get; set; }

        private void logout(string text)
        {
            Dispatcher.InvokeAsync(() =>
            {
                tbOutput.AppendText(text + Environment.NewLine);
                tbOutput.ScrollToEnd();
            });
        }

        private void UpdateProgressLabel(int filesCount)
        {
            string eta = string.Empty;
            if (pbProgress.Value > 0)
            {
                double completedUnits = showByteProgress && completeFilesLength > 0 ? completeFilesLength : pbProgress.Value;
                double totalUnits = showByteProgress && totalFilesLength > 0 ? totalFilesLength : filesCount;
                if (completedUnits > 0 && totalUnits >= completedUnits)
                {
                    DateTime etaValue = new DateTime(
                        (long)((DateTime.Now.Ticks - startTimestamp.Ticks) * (totalUnits / completedUnits - 1)));
                    eta = string.Format(" ETA: {0}:{1}", etaValue.Day - 1, etaValue.ToString("HH:mm:ss"));
                }
            }

            if (showByteProgress && totalFilesLength > 0)
            {
                lProgress.Content = string.Format("{0} of {1} files {2} ({3} of {4}){5}",
                    (int)pbProgress.Value, filesCount, GetProgressActionLabel(), ToBytes(completeFilesLength), ToBytes(totalFilesLength), eta);
            }
            else
            {
                lProgress.Content = string.Format("{0} of {1} files {2}{3}",
                    (int)pbProgress.Value, filesCount, GetProgressActionLabel(), eta);
            }
        }

        private string GetProgressActionLabel()
        {
            ExportMode exportMode = GetSelectedExportMode();
            if (exportMode == ExportMode.AuditReplayCoverage)
                return "audited";
            if (exportMode == ExportMode.Historical)
                return "exported";
            return "converted";
        }

        private void run(int filesCount, bool useByteProgress)
        {
            running = true;
            canceling = false;
            showByteProgress = useByteProgress;
            completeFilesLength = 0;
            if (!showByteProgress)
                totalFilesLength = 0;
            startTimestamp = DateTime.Now;

            Dispatcher.InvokeAsync(() =>
            {
                bConvert.IsEnabled = true;
                bConvert.Content = "_Cancel";
                tbCsvRootDir.IsReadOnly = true;
                if (cbExportMode != null)
                    cbExportMode.IsEnabled = false;
                if (cbHistoricalSeries != null)
                    cbHistoricalSeries.IsEnabled = false;
                dpStartDate.IsEnabled = false;
                dpEndDate.IsEnabled = false;
                cbInstrumentFilter.IsEnabled = false;
                double margin = (double)FindResource("MarginBase");
                lProgress.Margin = new Thickness(0, 0, 0, 0);
                lProgress.Height = 24;
                pbProgress.Margin = new Thickness((double)FindResource("MarginBase"));
                pbProgress.Height = 16;
                pbProgress.Minimum = 0;
                pbProgress.Maximum = filesCount;
                pbProgress.Value = 0;
            });
        }

        private void complete()
        {
            Dispatcher.InvokeAsync(() =>
            {
                running = false;
                canceling = false;
                lProgress.Margin = new Thickness(0);
                lProgress.Height = 0;
                pbProgress.Margin = new Thickness(0);
                pbProgress.Height = 0;
                tbCsvRootDir.IsReadOnly = false;
                if (cbExportMode != null)
                    cbExportMode.IsEnabled = true;
                if (cbHistoricalSeries != null)
                    cbHistoricalSeries.IsEnabled = true;
                dpStartDate.IsEnabled = true;
                dpEndDate.IsEnabled = true;
                cbInstrumentFilter.IsEnabled = true;
                bConvert.IsEnabled = true;
                bConvert.Content = GetSelectedExportMode() == ExportMode.AuditReplayCoverage ? "_Audit" : "_Convert";
            });
        }

        public static string ToBytes(long bytes)
        {
            if (bytes < 1024) return string.Format("{0} B", bytes);
            double exp = (int)(Math.Log(bytes) / Math.Log(1024));
            return string.Format("{0:F1} {1}iB", bytes / Math.Pow(1024, exp), "KMGTPE"[(int)exp - 1]);
        }
    }

    public class ReplayCsvWriteResult
    {
        public int WrittenRows { get; set; }
        public DateTime? FirstTimestampDate { get; set; }
        public DateTime? LastTimestampDate { get; set; }
    }

    public class ReplayContractFolder
    {
        public string DirectoryPath { get; set; }
        public string ContractFolder { get; set; }
        public string BaseInstrument { get; set; }
        public Cbi.Instrument Instrument { get; set; }
    }

    public class ReplayAuditEntry
    {
        public string ContractFolder { get; set; }
        public string BaseInstrument { get; set; }
        public Cbi.Instrument Instrument { get; set; }
        public DateTime? Date { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public bool FileExists { get; set; }
        public long FileBytes { get; set; }
        public DateTimeOffset? FirstTimestampEt { get; set; }
        public DateTimeOffset? LastTimestampEt { get; set; }
        public long Level1RowCount { get; set; }
        public int InvalidTimestampRows { get; set; }
        public bool IsDuplicate { get; set; }
        public string DuplicateContractFolders { get; set; }
        public ReplayAuditStatus Status { get; set; }
        public string Notes { get; set; }
    }

    public class ReplayAuditReportPaths
    {
        public string CsvPath { get; set; }
        public string TxtPath { get; set; }
    }

    public class DumpEntry
    {
        public long NrdLength { get; set; }
        public Cbi.Instrument Instrument { get; set; }
        public DateTime Date { get; set; }
        public string CsvFileName { get; set; }
        public string FromName { get; set; }
        public string ToName { get; set; }
    }

    public class HistoricalDumpEntry
    {
        public Cbi.Instrument Instrument { get; set; }
        public HistoricalSeriesKind SeriesKind { get; set; }
        public DateTime FromLocal { get; set; }
        public DateTime ToLocal { get; set; }
        public string CsvFileName { get; set; }
        public string FromName { get; set; }
        public string ToName { get; set; }
    }

    public enum ExportMode
    {
        Replay,
        AuditReplayCoverage,
        Historical,
    }

    public enum ReplayAuditStatus
    {
        OK,
        PARTIAL,
        MISSING,
        DUPLICATE,
        SUSPICIOUS,
    }

    public enum HistoricalSeriesKind
    {
        Tick,
        Minute,
        Day,
    }

    public class OptionItem<T>
    {
        public OptionItem(T value, string label)
        {
            Value = value;
            Label = label;
        }

        public T Value { get; private set; }
        public string Label { get; private set; }

        public override string ToString()
        {
            return Label;
        }
    }
}
