using System.Windows;
using MDTProNative.Wpf.Services;

namespace MDTProNative.Wpf.Windows;

public partial class NotepadWindow : Window
{
    public NotepadWindow()
    {
        InitializeComponent();
        NotesBox.Text = NotepadStore.Load();
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        NotepadStore.Save(NotesBox.Text);
        CadSaveSound.TryPlay();
        MdtShellEvents.LogCad("Notepad: saved to this computer.");
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
