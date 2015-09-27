using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using System.Windows.Forms;
using SuperPutty.Gui;
using SuperPutty.Data;
using System.Drawing;

namespace SuperPuttyUnitTests
{
    //[TestFixture]
    public class QuickSelectorTests
    {

        [TestView]
        public void Test()
        {
            SessionRoot root = SessionXmlFileSource.LoadSessionsFromFile("c:/Users/beau/SuperPuTTY/connections.xml", "Test");
            QuickSelectorData data = new QuickSelectorData();

            foreach (SessionLeaf sd in root.Flatten<SessionLeaf>())
            {
                data.ItemData.AddItemDataRow(
                    sd.Name, 
                    sd.GetFullPathToString(),
                    sd.Proto == ConnectionProtocol.Cygterm || sd.Proto == ConnectionProtocol.Mintty ? Color.Blue : Color.Black, sd);
            }

            QuickSelectorOptions opt = new QuickSelectorOptions();
            opt.Sort = data.ItemData.DetailColumn.ColumnName;
            opt.BaseText = "Open Session";

            QuickSelector d = new QuickSelector();
            d.ShowDialog(null, data, opt);
        }


    }
}
