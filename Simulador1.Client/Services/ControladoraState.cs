using System.Text;
using System.Timers;
using Microsoft.Extensions.DependencyInjection;

namespace Simulador1.Client.Services
{
    public class ControladoraState : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;

        private BancadaState _bancadaState => _serviceProvider.GetRequiredService<BancadaState>();

        private const int MAX_LINE_LENGTH = 28;
        private const int NUM_DISPLAY_LINES = 5;
        private const string SENHA_NIVEL2_CORRETA = "222222";
        private const int MAX_PASSWORD_LENGTH = 6;

        public event Action? OnChange;

        public bool IsOn { get; private set; }
        public List<string> CurrentDisplayLines { get; private set; } = new List<string>();
        public bool ShowTitleSeparatorLine { get; private set; }
        public int SelectedLineDisplayIndex { get; private set; } = -1;

        public bool LedFonteVerde { get; private set; }
        public bool LedFonteVermelho { get; private set; }
        public bool LedFalhaAmarelo { get; private set; }
        public bool LedAlarmeVermelho { get; private set; }
        public bool LedSupervisaoAmarelo { get; private set; }
        public DateTime Timestamp { get; set; }

        public enum Screen
        {
            Off,
            Welcome,
            SystemConfig,
            MainMenu,
            MenuPrincipal,
            GravarLerDispositivo,
            ConfigurarCentral,
            InformacaoGravarSucesso,
            FalhaDetectorFumaca,
            ListaDeFalhas,
            AlarmeGeralDisplay,
            SenhaNivel2,
            ConfirmarReiniciarCentral,
            ReiniciandoCentral,
            RegsEvent,
            EventHistory_Alarms,
            SenhaNivel2ParaSilenciarSirene,
            AdiarSirene,
            Info,
            UnderDevelopment
        }

        public Screen CurrentScreen
        {
            get => _currentScreen;
            private set
            {
                if (_currentScreen != value)
                {
                    _previousScreen = _currentScreen;
                    _currentScreen = value;
                    NotifyStateChanged();
                }
            }
        }
        private Screen _currentScreen = Screen.Off;
        private int _currentMenuSelection = 1;
        private int _currentMenuScrollOffset = 0;
        private Screen _previousScreen = Screen.Off;
        private Dictionary<Screen, Dictionary<string, object>> _screenStates = new Dictionary<Screen, Dictionary<string, object>>();

        private string _inputBuffer = "";
        private string _passwordInput = "";
        public string PasswordInput => new string('*', _passwordInput.Length);
        public bool IsReiniciandoCentral { get; private set; } = false;
        public bool IsSireneSilenciada { get; private set; } = false;
        public bool IsAlarmeGeral { get; private set; } = false;

        private Dictionary<int, string> _contextualButtonTexts = new Dictionary<int, string>
        {
            { 1, "" }, { 2, "" }, { 3, "" }, { 4, "" }
        };

        private readonly List<string> _mainMenuOptions = new List<string>
        {
            "1.Configurações",
            "2.Bloqueios",
            "3.Saídas",
            "4.Testes",
            "5.Registros de Eventos",
            "6.Informações do Sistema",
            "7.Operações de Rede"
        };

        private readonly List<string> _infoOptions = new List<string>
        {
            "1.Central de Incendio",
            "2.Modelo Central: CIE 1125",
            "3.Versão Central: 3.1.8",
            "4.Topologia: Classe A",
            "5.Retardo Maximo: 10:00",
            "6.Bloqueios Ativos: 0",
            "7.Saídas Ativas: 0",
            "8. Sir. Silenciadas: 0",
            "9. Data: {0}",
            "10. Hora: {0}",
            "11. Laço1: 4 Dispositivos",
            "12. Versão Fonte: 2.0.1",
            "13. Versão Laço: 0.12",
            "14. Versão Protocolo: 2.4"
        };

        private const int MENU_DISPLAY_COUNT = 4;
        public double ScrollHandleHeight { get; private set; }
        public double ScrollHandleTop { get; private set; }

        public int AlarmesCount => _alarmesCount;
        public int FalhasCount => _falhasCount;
        public int BloqueiosCount => _bloqueiosCount;
        public int SuperCount => _superCount;

        private int _alarmesCount = 0;
        private int _falhasCount = 0;
        private int _bloqueiosCount = 0;
        private int _superCount = 0;
        public string LastAlarmSource => _lastAlarmSource;
        private string _lastAlarmSource = "";
        private bool _isSmokeAlarmActive = false;
        private bool _isGasAlarmActive = false;
        private bool _isTempAlarmActive = false;
        private bool _isButtonAlarmActive = false;

        private List<string> _listaDeFalhas = new List<string>();

        public class AlarmEvent
        {
            public int Id { get; set; }
            public string SourceName { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }

        private List<AlarmEvent> _historicoAlarmes = new List<AlarmEvent>();
        private int _alarmHistoryScrollOffset = 0;
        private int _currentAlarmSelection = 1;

        private System.Timers.Timer? _reiniciarCentralTimer;
        private System.Timers.Timer? _clockUpdateTimer;
        private const int REINICIAR_CENTRAL_DELAY_MS = 3000;


        public ControladoraState(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            _bancadaState.OnChange += HandleBancadaStateChange;

            UpdateControladoraPowerState();

            _clockUpdateTimer = new System.Timers.Timer(1000);
            _clockUpdateTimer.Elapsed += (sender, e) =>
            {
                if (IsOn && (CurrentScreen == Screen.MainMenu || CurrentScreen == Screen.Info))
                {
                    UpdateDisplayForCurrentScreen();
                    NotifyStateChanged();
                }
            };
            _clockUpdateTimer.Start();
        }

        private void HandleBancadaStateChange()
        {
            UpdateControladoraPowerState();
            UpdateFaultState();
            CheckForAlarm();
            NotifyStateChanged();
        }

        public void CheckForAlarm()
        {
            if (_bancadaState.IsSireneAtiva || _alarmesCount > 0)
            {
                LedAlarmeVermelho = true;
            }
            else
            {
                LedAlarmeVermelho = false;
            }
        }

        public void DisplayAlarme(string source = "Alarme Geral")
        {
            if (!IsOn) return;

            bool incrementAlarmCount = false;

            if (source.Contains("Fumaça"))
            {
                if (!_isSmokeAlarmActive)
                {
                    incrementAlarmCount = true;
                    _isSmokeAlarmActive = true;
                }
            }
            else if (source.Contains("Gás"))
            {
                if (!_isGasAlarmActive)
                {
                    incrementAlarmCount = true;
                    _isGasAlarmActive = true;
                }
            }
            else if (source.Contains("Temperatura"))
            {
                if (!_isTempAlarmActive)
                {
                    incrementAlarmCount = true;
                    _isTempAlarmActive = true;
                }
            }
            else if (source.Contains("Botão de Incêndio"))
            {
                if (!_isButtonAlarmActive)
                {
                    incrementAlarmCount = true;
                    _isButtonAlarmActive = true;
                }
            }
            else if (source == "Alarme Geral")
            {
                if (!IsAlarmeGeral)
                {
                    incrementAlarmCount = true;
                }
            }
            else if (!_isSmokeAlarmActive && !_isGasAlarmActive && !_isTempAlarmActive && !_isButtonAlarmActive && !IsAlarmeGeral)
            {
                incrementAlarmCount = true;
            }

            if (incrementAlarmCount)
            {
                _alarmesCount++;
                var novoAlarme = new AlarmEvent
                {
                    Id = _historicoAlarmes.Count + 1,
                    SourceName = source,
                    Timestamp = DateTime.Now
                };
                _historicoAlarmes.Add(novoAlarme);
            }

            _lastAlarmSource = source;

            if (CurrentScreen != Screen.AlarmeGeralDisplay)
            {
                CurrentScreen = Screen.AlarmeGeralDisplay;
            }
            _bancadaState.AtivarAlarmeGeral();
            NotifyStateChanged();
        }

        private void UpdateControladoraPowerState()
        {
            if (_bancadaState.IsOn)
            {
                if (!IsOn)
                {
                    IsOn = true;
                    CurrentScreen = Screen.MainMenu;
                    DisplayMainMenu();

                    CheckExternalAlarmTriggers();
                }
                LedFonteVerde = true;
                LedFonteVermelho = false;
            }
            else
            {
                if (IsOn)
                {
                    IsOn = false;
                    CurrentDisplayLines.Clear();
                    _inputBuffer = "";
                    _passwordInput = "";
                    ResetContextualButtons();
                    ShowTitleSeparatorLine = false;
                    SelectedLineDisplayIndex = -1;
                    CurrentScreen = Screen.Off;
                    ResetAlarmCountersAndSirene();
                    IsSireneSilenciada = false;
                    IsAlarmeGeral = false;
                }
                LedFonteVerde = false;
                LedFonteVermelho = true;
            }
        }

        private void CheckExternalAlarmTriggers()
        {
            if (_bancadaState.IsPressionado)
            {
                DisplayAlarme("Botão de Incêndio");
            }
        }

        private void ResetAlarmCountersAndSirene()
        {
            _bancadaState.DesativarSirene();

            _alarmesCount = 0;
            _falhasCount = 0;
            _bloqueiosCount = 0;
            _superCount = 0;
            _listaDeFalhas.Clear();
            LedAlarmeVermelho = false;
            _lastAlarmSource = "";
            _isSmokeAlarmActive = false;
            _isGasAlarmActive = false;
            _isTempAlarmActive = false;
            _isButtonAlarmActive = false;
        }

        private void UpdateFaultState()
        {
            if (_bancadaState.DetectorFumacaEmFalha)
            {
                LedFalhaAmarelo = true;
                if (!_listaDeFalhas.Any(f => f.Contains("L1D010")))
                {
                    _listaDeFalhas.Add($"1. Z001L1D010 {DateTime.Now:dd/MM HH:mm}");
                    _falhasCount = _listaDeFalhas.Count;
                }

                if (CurrentScreen != Screen.FalhaDetectorFumaca && CurrentScreen != Screen.ListaDeFalhas && CurrentScreen != Screen.AlarmeGeralDisplay && CurrentScreen != Screen.SenhaNivel2 && CurrentScreen != Screen.ConfirmarReiniciarCentral && CurrentScreen != Screen.ReiniciandoCentral && CurrentScreen != Screen.SenhaNivel2ParaSilenciarSirene)
                {
                    CurrentScreen = Screen.FalhaDetectorFumaca;
                    DisplayFalhaDetectorFumacaScreen();
                }
                else if (CurrentScreen == Screen.FalhaDetectorFumaca)
                {
                    DisplayFalhaDetectorFumacaScreen();
                }
            }
            else
            {
                LedFalhaAmarelo = false;
                _listaDeFalhas.RemoveAll(f => f.Contains("L1D010"));
                _falhasCount = _listaDeFalhas.Count;

                if (CurrentScreen == Screen.FalhaDetectorFumaca && !_bancadaState.DetectorFumacaEmFalha)
                {
                    if (_listaDeFalhas.Count == 0)
                    {
                        if (CurrentScreen != Screen.AlarmeGeralDisplay && CurrentScreen != Screen.SenhaNivel2 && CurrentScreen != Screen.ConfirmarReiniciarCentral && CurrentScreen != Screen.ReiniciandoCentral && CurrentScreen != Screen.SenhaNivel2ParaSilenciarSirene)
                        {
                            CurrentScreen = _previousScreen == Screen.Off ? Screen.MainMenu : _previousScreen;
                            UpdateDisplayForCurrentScreen();
                        }
                    }
                    else if (CurrentScreen != Screen.AlarmeGeralDisplay && CurrentScreen != Screen.SenhaNivel2 && CurrentScreen != Screen.ConfirmarReiniciarCentral && CurrentScreen != Screen.ReiniciandoCentral && CurrentScreen != Screen.SenhaNivel2ParaSilenciarSirene)
                    {
                        CurrentScreen = Screen.ListaDeFalhas;
                        DisplayListaDeFalhasScreen();
                    }
                }
                if (CurrentScreen == Screen.ListaDeFalhas && CurrentScreen != Screen.AlarmeGeralDisplay && CurrentScreen != Screen.SenhaNivel2 && CurrentScreen != Screen.ConfirmarReiniciarCentral && CurrentScreen != Screen.ReiniciandoCentral && CurrentScreen != Screen.SenhaNivel2ParaSilenciarSirene)
                {
                    DisplayListaDeFalhasScreen();
                }
            }
        }

        private string FormatLine(string text, string alignment = "left")
        {
            if (string.IsNullOrEmpty(text))
                return new string(' ', MAX_LINE_LENGTH);

            if (text.Length > MAX_LINE_LENGTH)
                return text.Substring(0, MAX_LINE_LENGTH);

            switch (alignment)
            {
                case "center":
                    int padding = (MAX_LINE_LENGTH - text.Length) / 2;
                    return text.PadLeft(text.Length + padding, ' ').PadRight(MAX_LINE_LENGTH, ' ');
                case "right":
                    return text.PadLeft(MAX_LINE_LENGTH, ' ');
                case "left":
                default:
                    return text.PadRight(MAX_LINE_LENGTH, ' ');
            }
        }

        private void ResetContextualButtons()
        {
            _contextualButtonTexts[1] = "";
            _contextualButtonTexts[2] = "";
            _contextualButtonTexts[3] = "";
            _contextualButtonTexts[4] = "";
        }

        private void DisplayMainMenu()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("Intelbras" + new string(' ', MAX_LINE_LENGTH - "Intelbras".Length - "CIE 1125".Length) + "CIE 1125", "left"));
            CurrentDisplayLines.Add(FormatLine("Central Incêndio", "center"));
            CurrentDisplayLines.Add(FormatLine("Operação Normal", "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));

            DateTime now = DateTime.Now;
            string datePart = now.ToString("dd/MM/yy");
            string timePart = now.ToString("HH:mm:ss");

            string dateTimeLine = $"{datePart}" + new string(' ', MAX_LINE_LENGTH - datePart.Length - timePart.Length) + $"{timePart}";
            CurrentDisplayLines.Add(FormatLine(dateTimeLine, "left"));

            ShowTitleSeparatorLine = false;
            ResetContextualButtons();
            _contextualButtonTexts[1] = "Regs";
            _contextualButtonTexts[2] = "Teste";
            _contextualButtonTexts[3] = "Saída";
            _contextualButtonTexts[4] = "Info";
            SelectedLineDisplayIndex = -1;
        }

        private void DisplayMenuPrincipal()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("Menu" + new string(' ', MAX_LINE_LENGTH - "Menu".Length - "x/x".Length) + $"{_currentMenuSelection}/{_mainMenuOptions.Count}", "left"));
            ShowTitleSeparatorLine = true;

            int startIndex = _currentMenuScrollOffset;
            int endIndex = Math.Min(_currentMenuScrollOffset + MENU_DISPLAY_COUNT, _mainMenuOptions.Count);

            for (int i = 0; i < MENU_DISPLAY_COUNT; i++)
            {
                if (startIndex + i < _mainMenuOptions.Count)
                {
                    string option = _mainMenuOptions[startIndex + i];
                    CurrentDisplayLines.Add(FormatLine(option, "left"));
                }
                else
                {
                    CurrentDisplayLines.Add(FormatLine(""));
                }
            }

            ResetContextualButtons();
            _contextualButtonTexts[1] = "Voltar";
            _contextualButtonTexts[4] = "OK";
            SelectedLineDisplayIndex = (_currentMenuSelection - _currentMenuScrollOffset);

            UpdateScrollBar(_mainMenuOptions.Count, MENU_DISPLAY_COUNT);
        }

        private void UpdateScrollBar(int totalItems, int displayCount, int currentOffset = -1)
        {
            int offset = currentOffset == -1 ? _currentMenuScrollOffset : currentOffset;

            double contentAreaHeight = 132 - 5 - 5 - 15;
            contentAreaHeight = contentAreaHeight / NUM_DISPLAY_LINES * displayCount;

            if (totalItems > displayCount)
            {
                ScrollHandleHeight = contentAreaHeight * ((double)displayCount / totalItems);
                double maxScrollTop = contentAreaHeight - ScrollHandleHeight;
                ScrollHandleTop = maxScrollTop * ((double)offset / (totalItems - displayCount));
            }
            else
            {
                ScrollHandleHeight = contentAreaHeight;
                ScrollHandleTop = 0;
            }
        }

        private void DisplayRegsEvent()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("Registro de Eventos" + new string(' ', MAX_LINE_LENGTH - "Registro de Eventos".Length - $"{_currentMenuSelection}/4".Length) + $"{_currentMenuSelection}/4", "left"));
            CurrentDisplayLines.Add(FormatLine("1. Alarmes", "left"));
            CurrentDisplayLines.Add(FormatLine("2. Falhas", "left"));
            CurrentDisplayLines.Add(FormatLine("3. Supervisões", "left"));
            CurrentDisplayLines.Add(FormatLine("4. Operações", "left"));

            ShowTitleSeparatorLine = true;
            ResetContextualButtons();
            _contextualButtonTexts[1] = "Voltar";
            _contextualButtonTexts[4] = "OK";
            SelectedLineDisplayIndex = _currentMenuSelection;
        }

        private void DisplayEventHistoryAlarms()
        {
            CurrentDisplayLines.Clear();
            string headerTitle = "Alarmes";
            string counter = $"{(_historicoAlarmes.Count > 0 ? _currentAlarmSelection : 0)}/{_historicoAlarmes.Count}";
            int spaces = MAX_LINE_LENGTH - headerTitle.Length - counter.Length;
            string headerLine = headerTitle + new string(' ', Math.Max(0, spaces)) + counter;

            CurrentDisplayLines.Add(FormatLine(headerLine, "left"));
            ShowTitleSeparatorLine = true;

            int displayCount = 4;

            if (_historicoAlarmes.Count == 0)
            {
                CurrentDisplayLines.Add(FormatLine("Nenhum Alarme", "center"));
                for (int i = 0; i < displayCount - 1; i++) CurrentDisplayLines.Add(FormatLine(""));
                SelectedLineDisplayIndex = -1;
            }
            else
            {
                for (int i = 0; i < displayCount; i++)
                {
                    int index = _alarmHistoryScrollOffset + i;
                    if (index < _historicoAlarmes.Count)
                    {
                        var alarme = _historicoAlarmes[index];
                        string idStr = $"{alarme.Id}.";
                        string prefix = " *";
                        string dateTimeStr = $"{alarme.Timestamp:dd/MM HH:mm}";
                        int fixedLen = idStr.Length + prefix.Length + 1 + dateTimeStr.Length;
                        int availableNameLen = MAX_LINE_LENGTH - fixedLen;

                        string name = alarme.SourceName;
                        if (name.Length > availableNameLen) name = name.Substring(0, availableNameLen);

                        string lineContent = $"{idStr}{prefix}{name} {dateTimeStr}";
                        CurrentDisplayLines.Add(FormatLine(lineContent));
                    }
                    else
                    {
                        CurrentDisplayLines.Add(FormatLine(""));
                    }
                }
                SelectedLineDisplayIndex = (_currentAlarmSelection - _alarmHistoryScrollOffset);
            }

            ResetContextualButtons();
            _contextualButtonTexts[1] = "Voltar";
            _contextualButtonTexts[4] = "";

            UpdateScrollBar(_historicoAlarmes.Count, displayCount, _alarmHistoryScrollOffset);
        }

        private void DisplayInfo()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("Informações do Sistema" + new string(' ', MAX_LINE_LENGTH - "Informações do Sistema".Length - $"{_currentMenuSelection}/{_infoOptions.Count}".Length) + $"{_currentMenuSelection}/{_infoOptions.Count}", "left"));
            ShowTitleSeparatorLine = true;

            DateTime now = DateTime.Now;
            string currentDate = now.ToString("dd/MM/yyyy");
            string currentTime = now.ToString("HH:mm:ss");

            int startIndex = _currentMenuScrollOffset;
            int endIndex = Math.Min(_currentMenuScrollOffset + MENU_DISPLAY_COUNT, _infoOptions.Count);

            for (int i = 0; i < MENU_DISPLAY_COUNT; i++)
            {
                if (startIndex + i < _infoOptions.Count)
                {
                    string option = _infoOptions[startIndex + i];
                    if (option.StartsWith("9. Data:"))
                    {
                        option = string.Format(option, currentDate);
                    }
                    else if (option.StartsWith("10. Hora:"))
                    {
                        option = string.Format(option, currentTime);
                    }
                    CurrentDisplayLines.Add(FormatLine(option, "left"));
                }
                else
                {
                    CurrentDisplayLines.Add(FormatLine(""));
                }
            }

            ResetContextualButtons();
            _contextualButtonTexts[1] = "Voltar";
            SelectedLineDisplayIndex = (_currentMenuSelection - _currentMenuScrollOffset);
            UpdateScrollBar(_infoOptions.Count, MENU_DISPLAY_COUNT);
        }

        private void DisplayGravarLerDispositivo()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("Gravar/Ler Dispositivo 1/1", "left"));
            CurrentDisplayLines.Add(FormatLine("", "left"));
            CurrentDisplayLines.Add(FormatLine("Endereco:", "left"));
            CurrentDisplayLines.Add(FormatLine(_inputBuffer.PadRight(3, '_'), "left"));
            CurrentDisplayLines.Add(FormatLine("", "left"));

            while (CurrentDisplayLines.Count < NUM_DISPLAY_LINES)
            {
                CurrentDisplayLines.Add(FormatLine("", "left"));
            }

            ShowTitleSeparatorLine = false;
            ResetContextualButtons();
            _contextualButtonTexts[1] = "Apaga";
            _contextualButtonTexts[2] = "Ler";
            _contextualButtonTexts[3] = "Grava";
            SelectedLineDisplayIndex = -1;
        }

        private void DisplayInformacaoGravarSucesso()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("Informacao", "left"));
            CurrentDisplayLines.Add(FormatLine("", "left"));
            CurrentDisplayLines.Add(FormatLine("Endereco: " + _inputBuffer, "center"));
            CurrentDisplayLines.Add(FormatLine("Lido com sucesso", "center"));
            CurrentDisplayLines.Add(FormatLine("", "left"));

            while (CurrentDisplayLines.Count < NUM_DISPLAY_LINES)
            {
                CurrentDisplayLines.Add(FormatLine("", "left"));
            }

            ShowTitleSeparatorLine = false;
            ResetContextualButtons();
            _contextualButtonTexts[4] = "OK";
            SelectedLineDisplayIndex = -1;
        }

        private void DisplayFalhaDetectorFumacaScreen()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("Falha"));
            CurrentDisplayLines.Add(FormatLine("Disp. em Falha", "center"));
            CurrentDisplayLines.Add(FormatLine("Z001L1D010", "center"));
            CurrentDisplayLines.Add(FormatLine("Detector de Fumaça", "center"));
            CurrentDisplayLines.Add(FormatLine($"{DateTime.Now:dd/MM/yyyy HH:mm}", "center"));

            ShowTitleSeparatorLine = false;
            ResetContextualButtons();
            _contextualButtonTexts[1] = "Limpar";
            _contextualButtonTexts[4] = "Voltar";
            SelectedLineDisplayIndex = -1;
        }

        private void DisplayListaDeFalhasScreen()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("Lista de Falhas", "center"));
            ShowTitleSeparatorLine = true;
            for (int i = 0; i < NUM_DISPLAY_LINES - 1; i++)
            {
                if (i < _listaDeFalhas.Count)
                {
                    CurrentDisplayLines.Add(FormatLine(_listaDeFalhas[i]));
                }
                else
                {
                    CurrentDisplayLines.Add(FormatLine(""));
                }
            }

            ResetContextualButtons();
            _contextualButtonTexts[4] = "Voltar";
            SelectedLineDisplayIndex = -1;
        }

        private void DisplaySenhaNivel2Screen()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("SENHA NIVEL 2", "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));
            CurrentDisplayLines.Add(FormatLine("DIGITE A SENHA:", "center"));
            CurrentDisplayLines.Add(FormatLine(PasswordInput.PadRight(MAX_PASSWORD_LENGTH, '_'), "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));

            ResetContextualButtons();
            _contextualButtonTexts[1] = "Voltar";
            _contextualButtonTexts[2] = "Apaga";
            _contextualButtonTexts[3] = "Limpa";
            _contextualButtonTexts[4] = "OK";
            ShowTitleSeparatorLine = false;
            SelectedLineDisplayIndex = -1;
        }

        private void DisplayConfirmarReiniciarCentralScreen()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("REINICIAR CENTRAL?", "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));
            CurrentDisplayLines.Add(FormatLine("TEM CERTEZA?", "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));
            ResetContextualButtons();
            _contextualButtonTexts[1] = "Não";
            _contextualButtonTexts[4] = "Sim";
            ShowTitleSeparatorLine = false;
            SelectedLineDisplayIndex = -1;
        }

        private void DisplayReiniciandoCentralScreen()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("", "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));
            CurrentDisplayLines.Add(FormatLine("Reiniciando Central...", "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));
            ResetContextualButtons();
            ShowTitleSeparatorLine = false;
            SelectedLineDisplayIndex = -1;
        }

        private void DisplayAdiarSirene()
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine("Informação", "left"));
            CurrentDisplayLines.Add(FormatLine("", "center"));
            CurrentDisplayLines.Add(FormatLine("Nenhuma temporização", "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));


            ShowTitleSeparatorLine = true;
            ResetContextualButtons();
            _contextualButtonTexts[4] = "OK";
            SelectedLineDisplayIndex = -1;
        }

        private void DisplayUnderDevelopmentScreen(string title = "Em Desenvolvimento")
        {
            CurrentDisplayLines.Clear();
            CurrentDisplayLines.Add(FormatLine(title, "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));
            CurrentDisplayLines.Add(FormatLine("Recurso em", "center"));
            CurrentDisplayLines.Add(FormatLine("desenvolvimento...", "center"));
            CurrentDisplayLines.Add(FormatLine("", "center"));
            ShowTitleSeparatorLine = false;
            ResetContextualButtons();
            _contextualButtonTexts[4] = "Voltar";
            SelectedLineDisplayIndex = -1;
        }

        private void SaveCurrentScreenState(Screen screen)
        {
            _screenStates[screen] = new Dictionary<string, object>
            {
                { "CurrentMenuSelection", _currentMenuSelection },
                { "CurrentMenuScrollOffset", _currentMenuScrollOffset },
                { "InputBuffer", _inputBuffer },
                { "PasswordInput", _passwordInput }
            };
        }

        private void RestoreScreenState(Screen screen)
        {
            if (_screenStates.TryGetValue(screen, out var state))
            {
                _currentMenuSelection = (int)state["CurrentMenuSelection"];
                _currentMenuScrollOffset = (int)state["CurrentMenuScrollOffset"];
                _inputBuffer = (string)state["InputBuffer"];
                _passwordInput = (string)state["PasswordInput"];
            }
        }

        private void UpdateDisplayForCurrentScreen()
        {
            switch (CurrentScreen)
            {
                case Screen.Welcome:
                case Screen.MainMenu:
                    DisplayMainMenu();
                    break;
                case Screen.MenuPrincipal:
                    DisplayMenuPrincipal();
                    break;
                case Screen.GravarLerDispositivo:
                    DisplayGravarLerDispositivo();
                    break;
                case Screen.ConfigurarCentral:
                    DisplayUnderDevelopmentScreen("Configurar Central");
                    break;
                case Screen.SystemConfig:
                    DisplayUnderDevelopmentScreen("Configurações");
                    break;
                case Screen.InformacaoGravarSucesso:
                    DisplayInformacaoGravarSucesso();
                    break;
                case Screen.FalhaDetectorFumaca:
                    DisplayFalhaDetectorFumacaScreen();
                    break;
                case Screen.ListaDeFalhas:
                    DisplayListaDeFalhasScreen();
                    break;
                case Screen.AlarmeGeralDisplay:
                    CurrentDisplayLines.Clear();
                    CurrentDisplayLines.Add(FormatLine("ALARMES", "center"));
                    CurrentDisplayLines.Add(FormatLine(_lastAlarmSource, "center"));
                    if (_bancadaState.IsSireneAtiva)
                    {
                        CurrentDisplayLines.Add(FormatLine("SIRENE ATIVADA!", "center"));
                    }
                    else
                    {
                        CurrentDisplayLines.Add(FormatLine("Sirene Silenciada", "center"));
                    }
                    CurrentDisplayLines.Add(FormatLine($"Contagem: {_alarmesCount}", "center"));
                    CurrentDisplayLines.Add(FormatLine("", "center"));
                    ResetContextualButtons();
                    _contextualButtonTexts[1] = "Regs";
                    _contextualButtonTexts[4] = "Voltar";
                    ShowTitleSeparatorLine = false;
                    SelectedLineDisplayIndex = -1;
                    break;
                case Screen.SenhaNivel2:
                case Screen.SenhaNivel2ParaSilenciarSirene:
                    DisplaySenhaNivel2Screen();
                    break;
                case Screen.ConfirmarReiniciarCentral:
                    DisplayConfirmarReiniciarCentralScreen();
                    break;
                case Screen.ReiniciandoCentral:
                    DisplayReiniciandoCentralScreen();
                    break;
                case Screen.RegsEvent:
                    DisplayRegsEvent();
                    break;
                case Screen.EventHistory_Alarms:
                    DisplayEventHistoryAlarms();
                    break;
                case Screen.Info:
                    DisplayInfo();
                    break;
                case Screen.UnderDevelopment:
                    DisplayUnderDevelopmentScreen();
                    break;
                default:
                    CurrentDisplayLines.Clear();
                    CurrentDisplayLines.Add(FormatLine("Tela não implementada"));
                    ResetContextualButtons();
                    break;
            }
        }

        public string GetContextualButtonText(int buttonNumber)
        {
            if (CurrentScreen == Screen.AlarmeGeralDisplay || CurrentScreen == Screen.SenhaNivel2 || CurrentScreen == Screen.ConfirmarReiniciarCentral || CurrentScreen == Screen.ReiniciandoCentral || CurrentScreen == Screen.SenhaNivel2ParaSilenciarSirene)
            {
                return _contextualButtonTexts.GetValueOrDefault(buttonNumber, "");
            }
            return _contextualButtonTexts.GetValueOrDefault(buttonNumber, "");
        }

        public void PressOkMenu()
        {
            if (!IsOn) return;

            if (CurrentScreen == Screen.SenhaNivel2 || CurrentScreen == Screen.SenhaNivel2ParaSilenciarSirene)
            {
                if (_passwordInput == SENHA_NIVEL2_CORRETA)
                {
                    if (CurrentScreen == Screen.SenhaNivel2ParaSilenciarSirene)
                    {
                        IsSireneSilenciada = true;
                        _bancadaState.DesativarSirene();
                        CurrentScreen = _previousScreen;
                        UpdateDisplayForCurrentScreen();
                    }
                    else if (CurrentScreen == Screen.SenhaNivel2)
                    {
                        CurrentScreen = Screen.ConfirmarReiniciarCentral;
                        DisplayConfirmarReiniciarCentralScreen();
                    }
                }
                else
                {
                    _passwordInput = "";
                    DisplaySenhaNivel2Screen();
                }
            }
            else if (CurrentScreen == Screen.MainMenu)
            {
                CurrentScreen = Screen.MenuPrincipal;
                _currentMenuSelection = 1;
                _currentMenuScrollOffset = 0;
                DisplayMenuPrincipal();
            }
            else if (CurrentScreen == Screen.MenuPrincipal)
            {
                switch (_currentMenuSelection)
                {
                    case 1:
                        SaveCurrentScreenState(Screen.MenuPrincipal);
                        CurrentScreen = Screen.SystemConfig;
                        UpdateDisplayForCurrentScreen();
                        break;
                    case 2:
                        SaveCurrentScreenState(Screen.MenuPrincipal);
                        CurrentScreen = Screen.UnderDevelopment;
                        DisplayUnderDevelopmentScreen("Bloqueios");
                        break;
                    case 3:
                        SaveCurrentScreenState(Screen.MenuPrincipal);
                        CurrentScreen = Screen.UnderDevelopment;
                        DisplayUnderDevelopmentScreen("Saídas");
                        break;
                    case 4:
                        SaveCurrentScreenState(Screen.MenuPrincipal);
                        CurrentScreen = Screen.UnderDevelopment;
                        DisplayUnderDevelopmentScreen("Testes");
                        break;
                    case 5:
                        CurrentScreen = Screen.RegsEvent;
                        _currentMenuSelection = 1;
                        DisplayRegsEvent();
                        break;
                    case 6:
                        CurrentScreen = Screen.Info;
                        _currentMenuSelection = 1;
                        _currentMenuScrollOffset = 0;
                        DisplayInfo();
                        break;
                    case 7:
                        SaveCurrentScreenState(Screen.MenuPrincipal);
                        CurrentScreen = Screen.UnderDevelopment;
                        DisplayUnderDevelopmentScreen("Operações de Rede");
                        break;
                }
            }
            else if (CurrentScreen == Screen.SystemConfig)
            {
                CurrentScreen = Screen.MenuPrincipal;
                DisplayMenuPrincipal();
            }
            else if (CurrentScreen == Screen.ConfigurarCentral)
            {
                CurrentScreen = Screen.MenuPrincipal;
                DisplayMenuPrincipal();
            }
            else if (CurrentScreen == Screen.InformacaoGravarSucesso)
            {
                CurrentScreen = Screen.GravarLerDispositivo;
                DisplayGravarLerDispositivo();
            }
            else if (CurrentScreen == Screen.ListaDeFalhas)
            {
                CurrentScreen = _previousScreen == Screen.Off ? Screen.MainMenu : _previousScreen;
                UpdateDisplayForCurrentScreen();
            }
            else if (CurrentScreen == Screen.RegsEvent)
            {
                switch (_currentMenuSelection)
                {
                    case 1:
                        SaveCurrentScreenState(Screen.RegsEvent);
                        CurrentScreen = Screen.EventHistory_Alarms;
                        _alarmHistoryScrollOffset = 0;
                        _currentAlarmSelection = 1;
                        DisplayEventHistoryAlarms();
                        break;
                    case 2:
                        SaveCurrentScreenState(Screen.RegsEvent);
                        CurrentScreen = Screen.UnderDevelopment;
                        DisplayUnderDevelopmentScreen("Falhas");
                        break;
                    case 3:
                        SaveCurrentScreenState(Screen.RegsEvent);
                        CurrentScreen = Screen.UnderDevelopment;
                        DisplayUnderDevelopmentScreen("Supervisões");
                        break;
                    case 4:
                        SaveCurrentScreenState(Screen.RegsEvent);
                        CurrentScreen = Screen.UnderDevelopment;
                        DisplayUnderDevelopmentScreen("Operações");
                        break;
                }
            }
            else if (CurrentScreen == Screen.AdiarSirene)
            {
                CurrentScreen = _previousScreen;
                UpdateDisplayForCurrentScreen();
            }
            else if (CurrentScreen == Screen.UnderDevelopment)
            {
                CurrentScreen = _previousScreen;
                RestoreScreenState(CurrentScreen);
                UpdateDisplayForCurrentScreen();
            }
            NotifyStateChanged();
        }

        public void PressNumber(int number)
        {
            if (!IsOn) return;

            if (CurrentScreen == Screen.GravarLerDispositivo)
            {
                if (_inputBuffer.Length < 3)
                {
                    _inputBuffer += number.ToString();
                    DisplayGravarLerDispositivo();
                }
            }
            else if (CurrentScreen == Screen.SenhaNivel2 || CurrentScreen == Screen.SenhaNivel2ParaSilenciarSirene)
            {
                if (_passwordInput.Length < MAX_PASSWORD_LENGTH)
                {
                    _passwordInput += number.ToString();
                    DisplaySenhaNivel2Screen();
                }
            }
            NotifyStateChanged();
        }

        public void PressBackButton()
        {
            if (!IsOn) return;

            if (CurrentScreen == Screen.AlarmeGeralDisplay)
            {
                if (_bancadaState.IsSireneAtiva)
                {
                    return;
                }
                else
                {
                    CurrentScreen = Screen.MainMenu;
                    DisplayMainMenu();
                    NotifyStateChanged();
                    return;
                }
            }
            else if (CurrentScreen == Screen.RegsEvent)
            {
                CurrentScreen = Screen.MainMenu;
                DisplayMainMenu();
                NotifyStateChanged();
                return;
            }
            else if (CurrentScreen == Screen.EventHistory_Alarms)
            {
                if (IsAlarmeGeral || _alarmesCount > 0)
                {
                    CurrentScreen = Screen.AlarmeGeralDisplay;
                    UpdateDisplayForCurrentScreen();
                }
                else
                {
                    CurrentScreen = Screen.RegsEvent;
                    DisplayRegsEvent();
                }
                NotifyStateChanged();
                return;
            }
            else if (CurrentScreen == Screen.Info)
            {
                CurrentScreen = Screen.MainMenu;
                DisplayMainMenu();
                NotifyStateChanged();
                return;
            }
            else if (CurrentScreen == Screen.SenhaNivel2 || CurrentScreen == Screen.SenhaNivel2ParaSilenciarSirene)
            {
                CurrentScreen = _previousScreen;
                _passwordInput = "";
                UpdateDisplayForCurrentScreen();
                NotifyStateChanged();
                return;
            }
            else if (CurrentScreen == Screen.ConfirmarReiniciarCentral)
            {
                if (AlarmesCount > 0)
                {
                    CurrentScreen = Screen.AlarmeGeralDisplay;
                    UpdateDisplayForCurrentScreen();
                    NotifyStateChanged();
                    return;
                }
                else
                {
                    CurrentScreen = Screen.MainMenu;
                    DisplayMainMenu();
                    NotifyStateChanged();
                    return;
                }
            }
            else if (CurrentScreen == Screen.AdiarSirene)
            {
                CurrentScreen = _previousScreen;
                UpdateDisplayForCurrentScreen();
                NotifyStateChanged();
                return;
            }
            else if (CurrentScreen == Screen.MenuPrincipal)
            {
                CurrentScreen = Screen.MainMenu;
                DisplayMainMenu();
                NotifyStateChanged();
                return;
            }
            else if (CurrentScreen == Screen.UnderDevelopment)
            {
                CurrentScreen = _previousScreen;
                RestoreScreenState(CurrentScreen);
                UpdateDisplayForCurrentScreen();
                NotifyStateChanged();
                return;
            }

            switch (CurrentScreen)
            {
                case Screen.SystemConfig:
                    CurrentScreen = Screen.MenuPrincipal;
                    DisplayMenuPrincipal();
                    break;
                case Screen.GravarLerDispositivo:
                case Screen.ConfigurarCentral:
                    CurrentScreen = Screen.MenuPrincipal;
                    DisplayMenuPrincipal();
                    break;
                case Screen.InformacaoGravarSucesso:
                    CurrentScreen = Screen.GravarLerDispositivo;
                    DisplayGravarLerDispositivo();
                    break;
                case Screen.FalhaDetectorFumaca:
                    CurrentScreen = _previousScreen == Screen.Off ? Screen.MainMenu : _previousScreen;
                    UpdateDisplayForCurrentScreen();
                    break;
                case Screen.ListaDeFalhas:
                    CurrentScreen = _previousScreen == Screen.Off ? Screen.MainMenu : _previousScreen;
                    UpdateDisplayForCurrentScreen();
                    break;
                default:
                    break;
            }
            NotifyStateChanged();
        }

        public void MoveMenuSelection(int direction)
        {
            if (!IsOn) return;

            if (CurrentScreen == Screen.MenuPrincipal)
            {
                int maxSelection = _mainMenuOptions.Count;
                _currentMenuSelection += direction;

                if (_currentMenuSelection < 1)
                {
                    _currentMenuSelection = maxSelection;
                    _currentMenuScrollOffset = Math.Max(0, _mainMenuOptions.Count - MENU_DISPLAY_COUNT);
                }
                else if (_currentMenuSelection > maxSelection)
                {
                    _currentMenuSelection = 1;
                    _currentMenuScrollOffset = 0;
                }
                else
                {
                    if (_currentMenuSelection - 1 < _currentMenuScrollOffset)
                    {
                        _currentMenuScrollOffset = _currentMenuSelection - 1;
                    }
                    else if (_currentMenuSelection - 1 >= _currentMenuScrollOffset + MENU_DISPLAY_COUNT)
                    {
                        _currentMenuScrollOffset = _currentMenuSelection - MENU_DISPLAY_COUNT;
                    }
                }
                DisplayMenuPrincipal();
            }
            else if (CurrentScreen == Screen.RegsEvent)
            {
                int maxSelection = 4;
                _currentMenuSelection = _currentMenuSelection + direction;
                if (_currentMenuSelection < 1) _currentMenuSelection = maxSelection;
                else if (_currentMenuSelection > maxSelection) _currentMenuSelection = 1;
                DisplayRegsEvent();
            }
            else if (CurrentScreen == Screen.Info)
            {
                int maxSelection = _infoOptions.Count;
                _currentMenuSelection += direction;

                if (_currentMenuSelection < 1)
                {
                    _currentMenuSelection = maxSelection;
                    _currentMenuScrollOffset = Math.Max(0, _infoOptions.Count - MENU_DISPLAY_COUNT);
                }
                else if (_currentMenuSelection > maxSelection)
                {
                    _currentMenuSelection = 1;
                    _currentMenuScrollOffset = 0;
                }
                else
                {
                    if (_currentMenuSelection - 1 < _currentMenuScrollOffset)
                    {
                        _currentMenuScrollOffset = _currentMenuSelection - 1;
                    }
                    else if (_currentMenuSelection - 1 >= _currentMenuScrollOffset + MENU_DISPLAY_COUNT)
                    {
                        _currentMenuScrollOffset = _currentMenuSelection - MENU_DISPLAY_COUNT;
                    }
                }
                DisplayInfo();
            }
            NotifyStateChanged();
        }

        public void PressNavigateUp()
        {
            if (!IsOn) return;

            if (CurrentScreen == Screen.EventHistory_Alarms)
            {
                if (_historicoAlarmes.Count > 0)
                {
                    _currentAlarmSelection--;
                    if (_currentAlarmSelection < 1) _currentAlarmSelection = 1;

                    if (_currentAlarmSelection <= _alarmHistoryScrollOffset)
                    {
                        _alarmHistoryScrollOffset = Math.Max(0, _currentAlarmSelection - 1);
                    }
                    DisplayEventHistoryAlarms();
                    NotifyStateChanged();
                }
            }
            else
            {
                MoveMenuSelection(-1);
            }
        }

        public void PressNavigateDown()
        {
            if (!IsOn) return;

            if (CurrentScreen == Screen.EventHistory_Alarms)
            {
                if (_historicoAlarmes.Count > 0)
                {
                    _currentAlarmSelection++;
                    if (_currentAlarmSelection > _historicoAlarmes.Count) _currentAlarmSelection = _historicoAlarmes.Count;

                    if (_currentAlarmSelection > _alarmHistoryScrollOffset + 4)
                    {
                        _alarmHistoryScrollOffset = _currentAlarmSelection - 4;
                    }

                    DisplayEventHistoryAlarms();
                    NotifyStateChanged();
                }
            }
            else
            {
                MoveMenuSelection(1);
            }
        }

        public void PressNavigateLeft()
        {
            if (!IsOn) return;
            NotifyStateChanged();
        }

        public void PressNavigateRight()
        {
            if (!IsOn) return;
            NotifyStateChanged();
        }

        public void PressContextualButton1()
        {
            if (!IsOn) return;

            if (CurrentScreen == Screen.SenhaNivel2 || CurrentScreen == Screen.SenhaNivel2ParaSilenciarSirene)
            {
                PressBackButton();
            }
            else if (CurrentScreen == Screen.ConfirmarReiniciarCentral)
            {
                if (AlarmesCount > 0)
                {
                    CurrentScreen = Screen.AlarmeGeralDisplay;
                    UpdateDisplayForCurrentScreen();
                    NotifyStateChanged();
                    return;
                }
                else
                {
                    CurrentScreen = Screen.MainMenu;
                    DisplayMainMenu();
                    NotifyStateChanged();
                    return;
                }
            }
            else if (CurrentScreen == Screen.GravarLerDispositivo && _contextualButtonTexts[1].StartsWith("Apaga"))
            {
                if (_inputBuffer.Length > 0)
                {
                    _inputBuffer = _inputBuffer.Remove(_inputBuffer.Length - 1);
                    DisplayGravarLerDispositivo();
                }
            }
            else if (CurrentScreen == Screen.AlarmeGeralDisplay && _contextualButtonTexts[1] == "Regs")
            {
                CurrentScreen = Screen.EventHistory_Alarms;
                _alarmHistoryScrollOffset = 0;
                _currentAlarmSelection = 1;
                DisplayEventHistoryAlarms();
            }
            else if (CurrentScreen == Screen.MainMenu && _contextualButtonTexts[1].StartsWith("Regs"))
            {
                CurrentScreen = Screen.RegsEvent;
                _currentMenuSelection = 1;
                DisplayRegsEvent();
            }
            else if (CurrentScreen == Screen.RegsEvent && _contextualButtonTexts[1].StartsWith("Voltar"))
            {
                PressBackButton();
            }
            else if (CurrentScreen == Screen.AlarmeGeralDisplay && _contextualButtonTexts[1].StartsWith("Silenciar"))
            {
                PressSilenciarSirene();
            }
            else if (CurrentScreen == Screen.MenuPrincipal && _contextualButtonTexts[1].StartsWith("Voltar"))
            {
                PressBackButton();
            }
            else if (CurrentScreen == Screen.Info && _contextualButtonTexts[1].StartsWith("Voltar"))
            {
                PressBackButton();
            }
            else if (CurrentScreen == Screen.EventHistory_Alarms && _contextualButtonTexts[1].StartsWith("Voltar"))
            {
                PressBackButton();
            }
            NotifyStateChanged();
        }

        public void PressContextualButton2()
        {
            if (!IsOn) return;

            if (CurrentScreen == Screen.SenhaNivel2 || CurrentScreen == Screen.SenhaNivel2ParaSilenciarSirene)
            {
                if (_passwordInput.Length > 0)
                {
                    _passwordInput = _passwordInput.Remove(_passwordInput.Length - 1);
                    DisplaySenhaNivel2Screen();
                }
            }
            else if (CurrentScreen == Screen.GravarLerDispositivo && _contextualButtonTexts[2].StartsWith("Ler"))
            {
                if (!string.IsNullOrEmpty(_inputBuffer))
                {
                    CurrentScreen = Screen.InformacaoGravarSucesso;
                    DisplayInformacaoGravarSucesso();
                }
            }
            NotifyStateChanged();
        }

        public void PressContextualButton3()
        {
            if (!IsOn) return;

            if (CurrentScreen == Screen.SenhaNivel2 || CurrentScreen == Screen.SenhaNivel2ParaSilenciarSirene)
            {
                _passwordInput = "";
                DisplaySenhaNivel2Screen();
            }
            else if (CurrentScreen == Screen.GravarLerDispositivo && _contextualButtonTexts[3].StartsWith("Grava"))
            {
                if (!string.IsNullOrEmpty(_inputBuffer))
                {
                    CurrentScreen = Screen.InformacaoGravarSucesso;
                    DisplayInformacaoGravarSucesso();
                }
            }
            NotifyStateChanged();
        }

        public void PressContextualButton4()
        {
            if (!IsOn) return;

            if (CurrentScreen == Screen.SenhaNivel2 || CurrentScreen == Screen.SenhaNivel2ParaSilenciarSirene)
            {
                PressOkMenu();
            }
            else if (CurrentScreen == Screen.ConfirmarReiniciarCentral)
            {
                StartReiniciarCentralProcess();
            }
            else if (CurrentScreen == Screen.ListaDeFalhas && GetContextualButtonText(4).Contains("Voltar"))
            {
                PressBackButton();
            }
            else if (CurrentScreen == Screen.FalhaDetectorFumaca && GetContextualButtonText(4).Contains("Voltar"))
            {
                PressBackButton();
            }
            else if ((CurrentScreen == Screen.Welcome && _contextualButtonTexts[4].Contains("Info")) ||
                     (CurrentScreen == Screen.SystemConfig && _contextualButtonTexts[4].Contains("Voltar")) ||
                     (CurrentScreen == Screen.ConfigurarCentral && _contextualButtonTexts[4].Contains("Voltar")) ||
                     (CurrentScreen == Screen.InformacaoGravarSucesso && _contextualButtonTexts[4].Contains("OK")) ||
                     (CurrentScreen == Screen.UnderDevelopment && _contextualButtonTexts[4].Contains("Voltar")))
            {
                PressOkMenu();
            }
            else if (CurrentScreen == Screen.MainMenu && _contextualButtonTexts[4].Contains("Info"))
            {
                CurrentScreen = Screen.Info;
                _currentMenuSelection = 1;
                _currentMenuScrollOffset = 0;
                DisplayInfo();
            }
            else if (CurrentScreen == Screen.RegsEvent && _contextualButtonTexts[4].Contains("OK"))
            {
                PressOkMenu();
            }
            else if (CurrentScreen == Screen.AdiarSirene && _contextualButtonTexts[4].Contains("OK"))
            {
                PressOkMenu();
            }
            else if (CurrentScreen == Screen.MenuPrincipal && _contextualButtonTexts[4].Contains("OK"))
            {
                PressOkMenu();
            }
            else if (CurrentScreen == Screen.AlarmeGeralDisplay && _contextualButtonTexts[4].Contains("Voltar"))
            {
                PressBackButton();
            }
            NotifyStateChanged();
        }

        public void PressReiniciarCentral()
        {
            if (!IsOn || IsReiniciandoCentral) return;

            CurrentScreen = Screen.SenhaNivel2;
            _passwordInput = "";
            DisplaySenhaNivel2Screen();
            NotifyStateChanged();
        }

        private void StartReiniciarCentralProcess()
        {
            _reiniciarCentralTimer?.Dispose();
            _bancadaState.DesativarSirene();
            CurrentScreen = Screen.ReiniciandoCentral;
            IsReiniciandoCentral = true;
            IsSireneSilenciada = false;
            IsAlarmeGeral = false;
            NotifyStateChanged();
            DisplayReiniciandoCentralScreen();


            _reiniciarCentralTimer = new System.Timers.Timer(REINICIAR_CENTRAL_DELAY_MS);
            _reiniciarCentralTimer.Elapsed += OnReiniciarCentralTimerElapsed;
            _reiniciarCentralTimer.AutoReset = false;
            _reiniciarCentralTimer.Start();
        }

        private void OnReiniciarCentralTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            _reiniciarCentralTimer?.Dispose();
            _reiniciarCentralTimer = null;

            ResetAlarmCountersAndSirene();
            DisplayMainMenu();
            CurrentScreen = Screen.MainMenu;
            IsReiniciandoCentral = false;

            CheckExternalAlarmTriggers();

            NotifyStateChanged();
        }

        public void PressBloqueios() { NotifyStateChanged(); }
        public void PressSilenciarSirene()
        {
            if (!IsOn) return;

            if (_bancadaState.IsSireneAtiva && CurrentScreen == Screen.AlarmeGeralDisplay)
            {
                CurrentScreen = Screen.SenhaNivel2ParaSilenciarSirene;
                _passwordInput = "";
                DisplaySenhaNivel2Screen();
                NotifyStateChanged();
            }
        }
        public void PressSireneBrigada() { NotifyStateChanged(); }
        public void PressSilenciarBipInterno() { NotifyStateChanged(); }
        public void PressAdiarSirene()
        {
            if (!IsOn) return;

            if (CurrentScreen != Screen.AdiarSirene)
            {
                CurrentScreen = Screen.AdiarSirene;
                DisplayAdiarSirene();
            }
            NotifyStateChanged();
        }
        public void PressAlarmeGeral()
        {
            if (!IsOn) return;
            DisplayAlarme("Alarme Geral");
            IsAlarmeGeral = true;
            NotifyStateChanged();
        }
        private void NotifyStateChanged() => OnChange?.Invoke();

        public void Dispose()
        {
            _bancadaState.OnChange -= HandleBancadaStateChange;
            if (_reiniciarCentralTimer != null)
            {
                _reiniciarCentralTimer.Elapsed -= OnReiniciarCentralTimerElapsed;
                _reiniciarCentralTimer.Dispose();
            }
            if (_clockUpdateTimer != null)
            {
                _clockUpdateTimer.Stop();
                _clockUpdateTimer.Dispose();
            }
        }
    }
}