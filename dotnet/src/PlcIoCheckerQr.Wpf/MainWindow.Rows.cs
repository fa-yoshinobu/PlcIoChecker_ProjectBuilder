using System.ComponentModel;
using System.Runtime.CompilerServices;
using PlcIoCheckerQr.Core;

namespace PlcIoCheckerQr.Wpf;

public partial class MainWindow
{
    public abstract class DataTypedAddressRow : INotifyPropertyChanged
    {
        private string _address = "";
        private string _comment = "";
        private string _dataType = "Bit";
        private bool _isUnsupportedDevice;
        private string _keyenceDeviceMode = "Normal";
        private string _vendor = "Melsec";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Address
        {
            get => _address;
            set
            {
                var next = value;
                if (_address == next)
                {
                    return;
                }

                _address = next;
                OnPropertyChanged();
                OnAddressChanged();
                RefreshDataTypeOptions();
            }
        }

        public string DataType
        {
            get => _dataType;
            set
            {
                var next = NormalizeDataType(value);
                if (_dataType == next)
                {
                    return;
                }

                _dataType = next;
                OnPropertyChanged();
            }
        }

        public string Comment
        {
            get => _comment;
            set
            {
                var next = value ?? "";
                if (_comment == next)
                {
                    return;
                }

                _comment = next;
                OnPropertyChanged();
            }
        }

        public bool IsUnsupportedDevice
        {
            get => _isUnsupportedDevice;
            set
            {
                if (_isUnsupportedDevice == value)
                {
                    return;
                }

                _isUnsupportedDevice = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<string> AvailableDataTypes =>
            ProjectFactory.DeviceDataTypesForAddress(Address, Vendor, KeyenceDeviceMode);

        protected string Vendor => _vendor;

        protected string KeyenceDeviceMode => _keyenceDeviceMode;

        public void SetVendor(string vendor) => SetDeviceContext(vendor, "Normal");

        public void SetDeviceContext(string vendor, string keyenceDeviceMode)
        {
            var nextVendor = string.IsNullOrWhiteSpace(vendor) ? "Melsec" : vendor;
            var nextMode = string.IsNullOrWhiteSpace(keyenceDeviceMode) ? "Normal" : keyenceDeviceMode;
            if (_vendor == nextVendor && _keyenceDeviceMode == nextMode)
            {
                return;
            }

            _vendor = nextVendor;
            _keyenceDeviceMode = nextMode;
            OnDeviceContextChanged();
            RefreshDataTypeOptions();
        }

        public void EnsureDataTypeAllowed() => DataType = NormalizeDataType(DataType);

        protected virtual void OnAddressChanged()
        {
        }

        protected virtual void OnDeviceContextChanged()
        {
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void RefreshDataTypeOptions()
        {
            OnPropertyChanged(nameof(AvailableDataTypes));
            EnsureDataTypeAllowed();
        }

        private string NormalizeDataType(string value)
        {
            var allowed = AvailableDataTypes;
            var match = allowed.FirstOrDefault(dataType => dataType.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }

            return string.IsNullOrWhiteSpace(Address)
                ? "Bit"
                : ProjectFactory.GuessDataType(Address, Vendor, KeyenceDeviceMode);
        }
    }

    public sealed class DeviceRow : DataTypedAddressRow
    {
    }

    public sealed class CommentRow : DataTypedAddressRow
    {
    }

    public sealed class WatchRow : DataTypedAddressRow
    {
    }

    public sealed class TrapConditionOption(string value)
    {
        public string Value { get; } = value;
        public string DisplayText { get; } = MainWindow.TrapConditionDisplayText(value);
    }

    public sealed class TrapRow : DataTypedAddressRow
    {
        private string _condition = "Change";
        private string _threshold = "";

        public string Condition
        {
            get => _condition;
            set => SetCondition(ProjectFactory.CoerceTrapConditionForAddress(Address, value, Vendor, KeyenceDeviceMode));
        }

        public string ConditionDisplayText => TrapConditionDisplayText(Condition);

        public IReadOnlyList<TrapConditionOption> AvailableConditionOptions =>
            ProjectFactory.TrapConditionsForAddress(Address, Vendor, KeyenceDeviceMode)
                .Select(condition => new TrapConditionOption(condition))
                .ToArray();

        public string Threshold
        {
            get => _threshold;
            set
            {
                var next = ThresholdEnabled ? value : "";
                if (_threshold == next)
                {
                    return;
                }

                _threshold = next;
                OnPropertyChanged();
            }
        }

        public bool ThresholdEnabled => ProjectFactory.TrapConditionRequiresThreshold(Condition);

        public bool Enabled { get; set; } = true;

        protected override void OnAddressChanged()
        {
            OnPropertyChanged(nameof(AvailableConditionOptions));
            CoerceCondition();
        }

        protected override void OnDeviceContextChanged()
        {
            OnPropertyChanged(nameof(AvailableConditionOptions));
            CoerceCondition();
        }

        private void CoerceCondition() => SetCondition(ProjectFactory.CoerceTrapConditionForAddress(Address, Condition, Vendor, KeyenceDeviceMode));

        private void SetCondition(string condition)
        {
            if (_condition == condition)
            {
                EnsureThresholdState();
                return;
            }

            _condition = condition;
            OnPropertyChanged(nameof(Condition));
            OnPropertyChanged(nameof(ConditionDisplayText));
            OnPropertyChanged(nameof(ThresholdEnabled));
            EnsureThresholdState();
        }

        private void EnsureThresholdState()
        {
            if (ProjectFactory.TrapConditionRequiresThreshold(Condition))
            {
                if (string.IsNullOrWhiteSpace(Threshold))
                {
                    Threshold = "0";
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(Threshold))
            {
                Threshold = "";
            }
        }

    }
}
