using System.IO;
using System.Xml.Serialization;

namespace EasyIDMMA.Converters
{
    public static class XmlConvert
    {
        public static T DeserializeFromFile<T>(string filePath)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var serializer = new XmlSerializer(typeof(T));
                return (T)serializer.Deserialize(fileStream);
            }
        }
    }
}