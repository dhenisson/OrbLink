using System.Windows;

namespace AMS.Transfer.App.Services;

public static class NativeFileDrag
{
    public static DataObject CreateDataObject(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("O arquivo recebido não está mais disponível.", path);
        var data = new DataObject();
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { Path.GetFullPath(path) });
        return data;
    }

    public static DragDropEffects Start(DependencyObject source, string path) =>
        DragDrop.DoDragDrop(source, CreateDataObject(path), DragDropEffects.Copy);
}
