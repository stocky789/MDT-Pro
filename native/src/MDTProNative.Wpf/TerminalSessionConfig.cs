namespace MDTProNative.Wpf;

/// <summary>Chosen at startup: who is at this terminal and which game PC to reach.</summary>
public sealed record TerminalSessionConfig(string TerminalDisplayName, string Host, int Port);
