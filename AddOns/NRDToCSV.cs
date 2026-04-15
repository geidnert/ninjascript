#region Using declarations
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
            Label lCsvRootDir = new Label()
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
                    new OptionItem<ExportMode>(ExportMode.Replay, "Replay market depth (.nrd -> L2 CSV)"),
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
                ToolTip = "Leave blank to convert all instruments. Enter a base instrument such as MNQ or NQ, or a full contract name.",
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
            bool isHistorical = GetSelectedExportMode() == ExportMode.Historical;
            if (lDateRange != null)
                lDateRange.Content = isHistorical
                    ? "Historical data date range to export:"
                    : "Data date range to convert (optional):";

            if (lHistoricalSeries != null)
                lHistoricalSeries.Visibility = isHistorical ? Visibility.Visible : Visibility.Collapsed;
            if (cbHistoricalSeries != null)
                cbHistoricalSeries.Visibility = isHistorical ? Visibility.Visible : Visibility.Collapsed;
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
                    logout("Canceling convertion...");
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
                DateTime outputDate = sourceDate.AddDays(1);

                if (selectedStartDate.HasValue && outputDate.Date < selectedStartDate.Value.Date)
                    continue;
                if (selectedEndDate.HasValue && outputDate.Date > selectedEndDate.Value.Date)
                    continue;

                string csvFileName = string.Format("{0}.csv", Path.Combine(csvDir, instrument.FullName, outputDate.ToString("yyyyMMdd")));
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
                if (instruments.Count != 1)
                    continue;

                string instrumentFilterName = GetInstrumentFilterName(instruments[0]);
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

        private string GetInstrumentFilterName(Cbi.Instrument instrument)
        {
            if (instrument == null)
                return string.Empty;

            if (instrument.MasterInstrument != null && !string.IsNullOrWhiteSpace(instrument.MasterInstrument.Name))
                return instrument.MasterInstrument.Name.Trim();

            return !string.IsNullOrWhiteSpace(instrument.FullName) ? instrument.FullName.Trim() : string.Empty;
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

            MarketReplay.DumpMarketDepth(entry.Instrument, entry.Date.AddDays(1), entry.Date.AddDays(1), entry.CsvFileName);

            logout(string.Format("Conversion \"{0}\" to \"{1}\" complete", entry.FromName, entry.ToName));
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
                lProgress.Content = string.Format("{0} of {1} files converted ({2} of {3}){4}",
                    (int)pbProgress.Value, filesCount, ToBytes(completeFilesLength), ToBytes(totalFilesLength), eta);
            }
            else
            {
                lProgress.Content = string.Format("{0} of {1} files converted{2}",
                    (int)pbProgress.Value, filesCount, eta);
            }
        }

        private void run(int filesCount, bool useByteProgress)
        {
            Dispatcher.InvokeAsync(() =>
            {
                running = true;
                canceling = false;
                showByteProgress = useByteProgress;
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
                completeFilesLength = 0;
                if (!showByteProgress)
                    totalFilesLength = 0;
                startTimestamp = DateTime.Now;
            });
        }

        private void complete()
        {
            Dispatcher.InvokeAsync(() =>
            {
                running = false;
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
                bConvert.Content = "_Convert";
            });
        }

        public static string ToBytes(long bytes)
        {
            if (bytes < 1024) return string.Format("{0} B", bytes);
            double exp = (int)(Math.Log(bytes) / Math.Log(1024));
            return string.Format("{0:F1} {1}iB", bytes / Math.Pow(1024, exp), "KMGTPE"[(int)exp - 1]);
        }
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
        Historical,
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
