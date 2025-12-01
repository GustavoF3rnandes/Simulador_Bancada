namespace Simulador1.Client.Services
{
    public class BancadaState
    {
        public bool IsOn { get; private set; } = false;
        public bool IsEmergencyActive { get; private set; } = false;
        public bool OnOffSwitchPosition { get; private set; } = false;
        public bool IsPressionado { get; private set; } = false;
        public bool IsSireneAtiva { get; private set; } = false;
        public bool DetectorFumacaEmFalha { get; private set; } = false;
        public bool DrLigado { get; private set; } = false;
        public bool McbLigado { get; private set; } = false;
        public bool IsAlarmeGeralAtivo { get; private set; } = false;

        public event Action? OnChange;

        public void ToggleDR()
        {
            DrLigado = !DrLigado;
            UpdateBancadaPowerState();
            NotifyStateChanged();
        }

        public void ToggleMCB()
        {
            McbLigado = !McbLigado;
            UpdateBancadaPowerState();
            NotifyStateChanged();
        }

        public void ToggleOnOffSwitch()
        {
            OnOffSwitchPosition = !OnOffSwitchPosition;
            UpdateBancadaPowerState();
            NotifyStateChanged();
        }

        private void UpdateBancadaPowerState()
        {
            if (DrLigado && McbLigado && OnOffSwitchPosition && !IsEmergencyActive)
            {
                IsOn = true;
            }
            else
            {
                IsOn = false;
                IsSireneAtiva = false;
            }
        }

        public void ToggleEmergency()
        {
            IsEmergencyActive = !IsEmergencyActive;
            UpdateBancadaPowerState();
            NotifyStateChanged();
        }

        public void Pressionar()
        {
            if (!IsPressionado)
            {
                IsPressionado = true;
                UpdateSireneState();
                NotifyStateChanged();
            }
        }

        public void ResetarBotao()
        {
            if (IsPressionado)
            {
                IsPressionado = false;
                NotifyStateChanged();
            }
        }

        public void AtivarSirene()
        {
            if (IsOn && IsPressionado && !IsSireneAtiva)
            {
                IsSireneAtiva = true;
                NotifyStateChanged();
            }
        }

        public void DesativarSirene()
        {
            if (IsSireneAtiva)
            {
                IsSireneAtiva = false;
                NotifyStateChanged();
            }
        }

        public void SetDetectorFumacaFalha(bool emFalha)
        {
            DetectorFumacaEmFalha = emFalha;
            NotifyStateChanged();
        }

        public void AtivarAlarmeGeral()
        {
            IsSireneAtiva = true;
            IsAlarmeGeralAtivo = true;
            UpdateSireneState();
            NotifyStateChanged();
        }

        public void DesativarAlarmeGeral()
        {
            IsSireneAtiva = false;
            IsAlarmeGeralAtivo = false;
            UpdateSireneState();
            NotifyStateChanged();
        }


        private void UpdateSireneState()
        {
            if (IsOn && IsPressionado || IsAlarmeGeralAtivo)
            {
                IsSireneAtiva = true;
            }
            else if (!IsOn || !IsAlarmeGeralAtivo)
            {
                IsSireneAtiva = false;
            }
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}