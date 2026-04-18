using System;
using System.Globalization;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters
{
    public sealed class MathConverter : IValueConverter
    {
        public static readonly MathConverter AddInstance = new MathConverter() { Operation = MathOperation.Add };
        public static readonly MathConverter SubtractInstance = new MathConverter() { Operation = MathOperation.Subtract };
        public static readonly MathConverter MultiplyInstance = new MathConverter() { Operation = MathOperation.Multiply };
        public static readonly MathConverter DivideInstance = new MathConverter() { Operation = MathOperation.Divide };
        public static readonly MathConverter PowInstance = new MathConverter() { Operation = MathOperation.Pow };

        public double Offset { get; set; }
        public MathOperation Operation { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                double value1 = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                double value2 = System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);

                object result = Binding.DoNothing;
                switch (Operation)
                {
                    case MathOperation.Add:
                        result = value1 + value2 + Offset;
                        break;
                    case MathOperation.Divide:
                        result = value1 / value2 + Offset;
                        break;
                    case MathOperation.Multiply:
                        result = value1 * value2 + Offset;
                        break;
                    case MathOperation.Subtract:
                        result = value1 - value2 + Offset;
                        break;
                    case MathOperation.Pow:
                        result = Math.Pow(value1, value2) + Offset;
                        break;
                    default:
                        result = Binding.DoNothing;
                        break;
                }

                return result;
            }
            catch (FormatException)
            {
                return Binding.DoNothing;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
