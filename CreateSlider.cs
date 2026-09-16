using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Opuntia
{
/*
 private void RunScript(
		bool run,
		List<string> sliderNames,
		double min,
		double max,
		int decimals,
		ref object sliders)
{
    List<Grasshopper.Kernel.Special.GH_NumberSlider> sliderList = 
        new List<Grasshopper.Kernel.Special.GH_NumberSlider>();

    int count = sliderNames.Count;
    int yOffset = 0;

    decimal minDec = (decimal)min;
    decimal maxDec = (decimal)max;

    for (int i = 0; i < count; i++)
    {
        var newSlider = new Grasshopper.Kernel.Special.GH_NumberSlider();
        newSlider.CreateAttributes();
        newSlider.Name = sliderNames[i];
        newSlider.NickName = sliderNames[i].Length > 0 
            ? sliderNames[i].Substring(0, Math.Min(30, sliderNames[i].Length)) 
            : "S" + i;

        decimal midDec = minDec + (maxDec - minDec) / 2m;
        newSlider.SetSliderValue(midDec);

        var type = newSlider.GetType();
        var sliderField = type.GetField("m_slider", 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);

        if (sliderField != null)
        {
            var sliderObj = sliderField.GetValue(newSlider);
            var sliderType = sliderObj.GetType();

            sliderType.GetProperty("Minimum").SetValue(sliderObj, minDec);
            sliderType.GetProperty("Maximum").SetValue(sliderObj, maxDec);
            sliderType.GetProperty("DecimalPlaces").SetValue(sliderObj, decimals);
        }

        newSlider.Attributes.Pivot = new PointF(0, yOffset);
        yOffset += 20;

        if (run)
            GrasshopperDocument.AddObject(newSlider, false);

        sliderList.Add(newSlider);
    }

    sliders = sliderList;
}
*/
}
