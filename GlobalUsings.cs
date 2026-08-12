// WPF + WinForms 混用（托盘）时的类型冲突消歧
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Clipboard = System.Windows.Clipboard;
