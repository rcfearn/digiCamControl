#region Licence

// Distributed under MIT License
// ===========================================================
// 
// digiCamControl - DSLR camera remote control open source software
// Copyright (C) 2014 Duka Istvan
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF 
// MERCHANTABILITY,FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY 
// CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH 
// THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

#endregion

#region

using CameraControl.Devices;
using CameraControl.Devices.Classes;

#endregion

namespace CameraControl.Core.Classes
{
    public class CameraPropertyEnumerator
    {
        public AsyncObservableCollection<CameraProperty> Items { get; set; }

        public CameraPropertyEnumerator()
        {
            Items = new AsyncObservableCollection<CameraProperty>();
        }

        public CameraProperty Get(ICameraDevice device)
        {
            if (device == null)
                return new CameraProperty();
            // there is a threading error, but too lazy to find it. 
            try
            {
                foreach (CameraProperty cameraProperty in Items)
                {
                    if (cameraProperty.SerialNumber == device.SerialNumber)
                        return cameraProperty;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("CameraProperty error", ex);
            }
            var c = new CameraProperty() {SerialNumber = device.SerialNumber, DeviceName = device.DisplayName};
            Items.Add(c);
            return c;
        }
    }
}