using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PCSXMemoryEditor.Entities
{
    public class ScanResult : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        // 将参数设为可选，并加上 [CallerMemberName] 特性
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }


        public string Address
        {
            get; set;
        }

        string _value=string.Empty;

        public string Value
        {
            get => _value;
            set
            {
                History9 = History8;
                History8 = History7;
                History7 = History6;
                History6 = History5;
                History5 = History4;
                History4 = History3;
                History3 = History2;
                History2 = History1;
                History1 = History0;
                History0 = Value;
                _value = value;
                OnPropertyChanged();
            }
        }
        public long RawAddress
        {
            get; set;
        }

        


        string history0 = string.Empty;

        public string History0
        {
            get => history0;
            private set => SetProperty(ref history0, value);
        }

        string history1 = string.Empty;

        public string History1
        {
            get => history1;
            private set => SetProperty(ref history1, value);
        }

        string history2 = string.Empty;

        public string History2
        {
            get => history2;
            private set => SetProperty(ref history2, value);
        }

        string history3 = string.Empty;

        public string History3
        {
            get => history3;
            private set => SetProperty(ref history3, value);
        }

        string history4 = string.Empty;

        public string History4
        {
            get => history4;
            private set => SetProperty(ref history4, value);
        }

        string history5 = string.Empty;

        public string History5
        {
            get => history5;
            private set => SetProperty(ref history5, value);
        }

        string history6 = string.Empty;

        public string History6
        {
            get => history6;
            private set => SetProperty(ref history6, value);
        }

        string history7 = string.Empty;

        public string History7
        {
            get => history7;
            private set => SetProperty(ref history7, value);
        }

        string history8 = string.Empty;

        public string History8
        {
            get => history8;
            private set => SetProperty(ref history8, value);
        }

        string history9 = string.Empty;

        public string History9
        {
            get => history9;
            private set => SetProperty(ref history9, value);
        }
    }
}
