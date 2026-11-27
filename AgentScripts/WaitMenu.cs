using MedSimAI.Core;
await Wait.Until(() => SessionManager.Instance != null && SessionManager.Instance.State == AppState.MainMenu);
return "state=" + SessionManager.Instance.State;
