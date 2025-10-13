using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace EinstellungenApp
{
    public partial class MainWindow : Window
    {
        private string _currentInput = "0";
        private double? _leftOperand;
        private string? _pendingOperation;
        private bool _resetInputOnNextDigit;

        public MainWindow()
        {
            InitializeComponent();
            DecimalButton.Content = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            UpdateDisplay();
        }

        private void DigitButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string digit)
            {
                if (_resetInputOnNextDigit || _currentInput == "0")
                {
                    _currentInput = digit;
                    _resetInputOnNextDigit = false;
                }
                else
                {
                    _currentInput += digit;
                }

                UpdateDisplay();
            }
        }

        private void DecimalButton_Click(object sender, RoutedEventArgs e)
        {
            var decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            if (_resetInputOnNextDigit)
            {
                _currentInput = "0";
                _resetInputOnNextDigit = false;
            }

            if (!_currentInput.Contains(decimalSeparator))
            {
                _currentInput += decimalSeparator;
                UpdateDisplay();
            }
        }

        private void OperationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string operation)
            {
                if (TryParseCurrentInput(out var value))
                {
                    if (_leftOperand.HasValue && _pendingOperation is not null && !_resetInputOnNextDigit)
                    {
                        value = Calculate(_leftOperand.Value, value, _pendingOperation);
                        _leftOperand = value;
                        DisplayText.Text = value.ToString(CultureInfo.CurrentCulture);
                    }
                    else
                    {
                        _leftOperand = value;
                    }

                    _pendingOperation = operation;
                    _resetInputOnNextDigit = true;
                }
            }
        }

        private void EqualsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingOperation is null || !_leftOperand.HasValue)
            {
                return;
            }

            if (TryParseCurrentInput(out var rightOperand))
            {
                try
                {
                    var result = Calculate(_leftOperand.Value, rightOperand, _pendingOperation);
                    _currentInput = result.ToString(CultureInfo.CurrentCulture);
                    _leftOperand = null;
                    _pendingOperation = null;
                    _resetInputOnNextDigit = true;
                    UpdateDisplay();
                }
                catch (DivideByZeroException)
                {
                    DisplayText.Text = "Fehler";
                    ResetState();
                }
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ResetState();
            UpdateDisplay();
        }

        private void ClearEntryButton_Click(object sender, RoutedEventArgs e)
        {
            _currentInput = "0";
            UpdateDisplay();
        }

        private void SignToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryParseCurrentInput(out var value))
            {
                value *= -1;
                _currentInput = value.ToString(CultureInfo.CurrentCulture);
                UpdateDisplay();
            }
        }

        private void UpdateDisplay()
        {
            DisplayText.Text = _currentInput;
        }

        private bool TryParseCurrentInput(out double value)
        {
            return double.TryParse(_currentInput, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private void ResetState()
        {
            _currentInput = "0";
            _leftOperand = null;
            _pendingOperation = null;
            _resetInputOnNextDigit = false;
        }

        private static double Calculate(double left, double right, string operation)
        {
            return operation switch
            {
                "+" => left + right,
                "-" => left - right,
                "*" => left * right,
                "/" when Math.Abs(right) < double.Epsilon => throw new DivideByZeroException(),
                "/" => left / right,
                _ => right
            };
        }
    }
}
