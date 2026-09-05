using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;
using System.Windows.Markup;
using System.Xml.Linq;

namespace SwiftExport.Client.Wpf.Tests.TestInfrastructure;

/// <summary>在已有 WPF Application 上按真实 App.xaml 顺序装载客户端资源。</summary>
internal static class ApplicationResourceLoader
{
    /// <summary>重建当前 Application 资源，供需要真实主题和命名样式的 STA 测试使用。</summary>
    public static void Load()
    {
        using var stream = typeof(ApplicationResourceLoader).Assembly
            .GetManifestResourceStream("DatePickerTests.App.xaml");
        Assert.IsNotNull(stream);
        var application = XDocument.Load(stream).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(application.Element(presentation + "Application.Resources")!.Elements().Single());
        foreach (var element in dictionary.DescendantsAndSelf())
        {
            var namespaceName = element.Name.NamespaceName;
            if (namespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal)
                && !namespaceName.Contains(";assembly=", StringComparison.Ordinal))
            {
                element.Name = XName.Get(element.Name.LocalName, namespaceName + ";assembly=SwiftExport.Client.Wpf");
            }
        }

        foreach (var attribute in application.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            var namespaceValue = attribute.Value;
            if (namespaceValue.StartsWith("clr-namespace:", StringComparison.Ordinal)
                && !namespaceValue.Contains(";assembly=", StringComparison.Ordinal))
            {
                namespaceValue += ";assembly=SwiftExport.Client.Wpf";
            }

            dictionary.SetAttributeValue(attribute.Name, namespaceValue);
        }

        var resources = new ResourceDictionary();
        Application.Current.Resources = resources;
        foreach (var mergedDictionary in dictionary.Element(presentation + "ResourceDictionary.MergedDictionaries")!.Elements())
        {
            var wrapper = new XElement(
                presentation + "ResourceDictionary",
                dictionary.Attributes(),
                new XElement(presentation + "ResourceDictionary.MergedDictionaries", new XElement(mergedDictionary)));
            var loaded = (ResourceDictionary)XamlReader.Parse(wrapper.ToString(), new ParserContext
            {
                BaseUri = new Uri("pack://application:,,,/SwiftExport.Client.Wpf;component/")
            });
            resources.MergedDictionaries.Add(loaded.MergedDictionaries.Single());
        }

        dictionary.Element(presentation + "ResourceDictionary.MergedDictionaries")!.Remove();
        var directResources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        foreach (var key in directResources.Keys)
        {
            resources.Add(key, directResources[key]);
        }
    }
}
