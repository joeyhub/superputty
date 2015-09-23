using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using System.Windows.Forms;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using log4net;
using System.Collections;

namespace SuperPutty.Utils
{
    /// <summary>
    /// PortableSettingsProvider
    /// 
    /// 
    /// Based on 
    /// http://www.codeproject.com/Articles/20917/Creating-a-Custom-Settings-Provider
    /// </summary>
    public class PortableSettingsProvider : SettingsProvider
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PortableSettingsProvider));

        private static bool ForceRoamingSettings = Convert.ToBoolean(ConfigurationManager.AppSettings["SuperPuTTY.ForceRoamingSettings"] ?? "True");

        public const string SettingsRoot = "Settings";

        private XmlDocument settingsXML;

        public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
        {
            base.Initialize(this.ApplicationName, config);
        }

        public override string ApplicationName
        {
            get
            {
                if (Application.ProductName.Trim().Length > 0)
                {
                    return Application.ProductName;
                }
                else
                {
                    FileInfo fi = new FileInfo(Application.ExecutablePath);
                    return fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length);
                }
            }
            set { }
        }


        /// <summary>
        /// Return a list of possible locations for the settings file.  If non are found, create the 
        /// default in the first location
        /// </summary>
        /// <returns></returns>
        public virtual string[] GetAppSettingsPaths()
        {
            string[] paths = new string[2];
            paths[0] = Environment.GetEnvironmentVariable("USERPROFILE");
            paths[1] = Path.GetDirectoryName(Application.ExecutablePath);
            return paths;
        }

        public virtual string GetAppSettingsFileName()
        {
            return ApplicationName + ".settings";
        }

        /// <summary>
        /// Return first existing file path or the first if none found
        /// </summary>
        /// <returns></returns>
        string GetAppSettingsFilePath()
        {
            string[] paths = GetAppSettingsPaths();
            string fileName = GetAppSettingsFileName();

            string path = Path.Combine(paths[0], fileName);
            foreach (string dir in paths)
            {
                string filePath = Path.Combine(dir, fileName);
                if (File.Exists(filePath))
                {
                    path = filePath;
                    break;
                }
            }
            return path;
        }

        public string SettingsFilePath { get; private set; }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            foreach (SettingsPropertyValue propVal in collection)
            {
                if(propVal.Name.Equals("OpenSessionWith"))
                    SetComplexValue(propVal);
                else
                    SetStringValue(propVal);
            }

            try
            {
                //SettingsXML.Save(Path.Combine(GetAppSettingsPath(), GetAppSettingsFileName()));
                SettingsXML.Save(GetAppSettingsFilePath());
            }
            catch(Exception ex){
                Log.Error("Error saving settings", ex);
            }
        }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            // Create new collection of values
            SettingsPropertyValueCollection values = new SettingsPropertyValueCollection();

            // Iterate through the settings to be retrieved
            foreach (SettingsProperty setting in collection)
            {
                SettingsPropertyValue value = new SettingsPropertyValue(setting);
                value.IsDirty = true;

                if(setting.Name.Equals("OpenSessionWith"))
                    value.PropertyValue = GetComplexValue(setting);
                else
                    value.SerializedValue = GetStringValue(setting);

                values.Add(value);
            }

            return values;
        }

        public XmlDocument SettingsXML
        {
            get
            {
                // If we dont hold an xml document, try opening one.
                // If it doesnt exist then create a new one ready.
                if (this.settingsXML == null)
                {
                    this.settingsXML = new XmlDocument();
                    //string settingsFile = Path.Combine(GetAppSettingsPath(), GetAppSettingsFileName());
                    string settingsFile = GetAppSettingsFilePath();
                    this.SettingsFilePath = settingsFile;
                    try
                    {
                        this.settingsXML.Load( settingsFile );
                        Log.InfoFormat("Loaded settings from {0}", settingsFile);
                    }
                    catch (Exception)
                    {
                        Log.InfoFormat("Could not load file ({0}), creating settings file", settingsFile);
                        // Create new document
                        XmlDeclaration declaration = this.settingsXML.CreateXmlDeclaration("1.0", "utf-8", String.Empty);
                        this.settingsXML.AppendChild(declaration);

                        XmlNode nodeRoot = this.settingsXML.CreateNode(XmlNodeType.Element, SettingsRoot, String.Empty);
                        this.settingsXML.AppendChild(nodeRoot);
                    }
                }

                return this.settingsXML;
            }
        }

        private XmlNode GetXmlNode(SettingsProperty setting, bool backwards)
        {
            XmlNode node = null;

            try
            {
                if (UseRoamingSettings(setting))
                {
                    node = SettingsXML.SelectSingleNode(SettingsRoot + "/" + setting.Name);

                    if (node == null && backwards)
                    {
                        // try go by host...backwards compatibility
                        node = SettingsXML.SelectSingleNode(SettingsRoot + "/" + GetHostName() + "/" + setting.Name);
                    }
                }
                else
                    node = SettingsXML.SelectSingleNode(SettingsRoot + "/" + GetHostName() + "/" + setting.Name);
            }
            catch(Exception e)
            {
                Log.WarnFormat("Exception loading node for {0}, {1}", setting.Name, e.Message);
            }

            return node;
        }

        private object GetComplexValue(SettingsProperty setting)
        {
            object value = setting.DefaultValue;

            XmlNode node = GetXmlNode(setting, true);

            if (node != null)
            {
                XmlSerializer xmlSerializer = new XmlSerializer(setting.PropertyType);

                using (StringReader reader = new StringReader(node.InnerXml))
                {
                    value = (object)xmlSerializer.Deserialize(reader);
                }
            }

            return value;
        }

        private string GetStringValue(SettingsProperty setting)
        {
            string value = String.Empty;

            XmlNode node = GetXmlNode(setting, true);

            if(node == null)
                if (setting.DefaultValue != null)
                    value = setting.DefaultValue.ToString();
                else
                    value = String.Empty;
            else
                value = node.InnerText;

            return value;
        }

        private XmlNode GetMachineNode()
        {
            XmlNode machineNode;

            // Its machine specific, store as an element of the machine name node,
            // creating a new machine name node if one doesnt exist.
            string nodePath = SettingsRoot + "/" + GetHostName();
            try
            {
                machineNode = (XmlElement)SettingsXML.SelectSingleNode(nodePath);
            }
            catch (Exception ex)
            {
                Log.Error("Error selecting node, " + nodePath, ex);
                machineNode = SettingsXML.CreateElement(GetHostName());
                SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(machineNode);
            }

            if (machineNode == null)
            {
                machineNode = SettingsXML.CreateElement(GetHostName());
                SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(machineNode);
            }

            return machineNode;
        }

        private void SetComplexValue(SettingsPropertyValue propVal)
        {
            XmlNode settingNode = GetXmlNode(propVal.Property, false);

            // Warning: Currently this doesn't loop. If more sources are added it will and will nuke everything.
            while (settingNode != null)
            {
                settingNode.ParentNode.RemoveChild(settingNode);
                settingNode = GetXmlNode(propVal.Property, false);
            }

            if (propVal.PropertyValue == null)
                return;

            XmlSerializer serializer = new XmlSerializer(propVal.Property.PropertyType);
            XmlElement valueNode = SettingsXML.CreateElement(propVal.Name);
            var settings = new XmlWriterSettings();
            settings.OmitXmlDeclaration = true;

            using (StringWriter stream = new StringWriter())
            using (XmlWriter writer = XmlWriter.Create(stream, settings))
            {
                serializer.Serialize(writer, propVal.PropertyValue);
                valueNode.InnerXml = stream.ToString();
            }

            if (UseRoamingSettings(propVal.Property))
                SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(valueNode);
            else
                GetMachineNode().AppendChild(valueNode);
        }

        private void SetStringValue(SettingsPropertyValue propVal)
        {
            XmlNode settingNode = GetXmlNode(propVal.Property, false);

            // Check to see if the node exists, if so then set its new value
            if (settingNode != null)
            {
                settingNode.InnerText = propVal.SerializedValue.ToString();
            }
            else
            {
                if (UseRoamingSettings(propVal.Property))
                {
                    // Store the value as an element of the Settings Root Node
                    settingNode = SettingsXML.CreateElement(propVal.Name);
                    settingNode.InnerText = propVal.SerializedValue.ToString();
                    SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(settingNode);
                }
                else
                {
                    settingNode = SettingsXML.CreateElement(propVal.Name);
                    settingNode.InnerText = propVal.SerializedValue.ToString();
                    GetMachineNode().AppendChild(settingNode);
                }
            }
        }

        private static string GetHostName()
        {
            return Environment.MachineName;
        }

        private bool UseRoamingSettings(SettingsProperty prop)
        {
            if (ForceRoamingSettings || string.IsNullOrEmpty(Environment.MachineName) || Char.IsDigit(Environment.MachineName[0]))
            {
                return true;
            }

            // Determine if the setting is marked as Roaming
            foreach (DictionaryEntry de in prop.Attributes)
            {
                Attribute attr = (Attribute) de.Value;
                if (attr is SettingsManageabilityAttribute)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
