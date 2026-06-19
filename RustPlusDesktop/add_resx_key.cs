using System;
using System.Xml.Linq;
using System.Linq;
using System.Text;
using System.IO;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length < 3) return;
        string filePath = args[0];
        string name = args[1];
        string value = args[2];
        
        var xdoc = XDocument.Load(filePath);
        var root = xdoc.Root;
        
        var existing = root.Elements("data").FirstOrDefault(e => e.Attribute("name")?.Value == name);
        if (existing != null)
        {
            existing.Element("value").Value = value;
        }
        else
        {
            root.Add(new XElement("data", 
                new XAttribute("name", name), 
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                new XElement("value", value)
            ));
        }
        
        // Write out explicitly with UTF-8 BOM
        using (var writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
        {
            xdoc.Save(writer);
        }
    }
}
