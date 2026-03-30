using System.Data;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

public static class JArrayToDataView
{
    public static DataView? Convert(JToken? token)
    {
        if (token is not JArray arr) return null;
        if (arr.Count == 0) return new DataTable().DefaultView;
        if (arr[0] is not JObject first) return null;

        var table = new DataTable();
        foreach (var p in first.Properties())
            table.Columns.Add(p.Name, typeof(string));

        foreach (var item in arr)
        {
            if (item is not JObject jo) continue;
            var row = table.NewRow();
            foreach (DataColumn col in table.Columns)
                row[col.ColumnName] = JTokenDisplay.ForDataCell(jo[col.ColumnName]);
            table.Rows.Add(row);
        }

        return table.DefaultView;
    }
}
