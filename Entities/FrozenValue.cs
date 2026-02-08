using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PCSXMemoryEditor.Entities
{
    public class FrozenValue : INotifyPropertyChanged
    {
        public long RawAddress
        {
            get; set;
        }
        public string Address
        {
            get; set;
        }
        public string TypeTag
        {
            get; set;
        } // "1":Byte, "2":Int16, "4":Int32, "Float":Float

        private string _value;
        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged(nameof(Value));
                    OnPropertyChanged(nameof(DisplayInfo));
                }
            }
        }

        public string DisplayInfo => $"[{GetTypeName()}] {Address} -> {Value}";

        private string GetTypeName()
        {
            switch (TypeTag)
            {
                case "1":
                    return "Byte";
                case "2":
                    return "Int16";
                case "4":
                    return "Int32";
                case "Float":
                    return "Float";
                default:
                    return "Unknown";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
