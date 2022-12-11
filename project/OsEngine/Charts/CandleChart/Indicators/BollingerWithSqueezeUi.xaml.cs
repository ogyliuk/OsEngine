﻿/*
 * Your rights to use code governed by this license http://o-s-a.net/doc/license_simple_engine.pdf
 *Ваши права на использования кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Windows;
using System.Windows.Forms;
using OsEngine.Language;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Forms.TextBox;

namespace OsEngine.Charts.CandleChart.Indicators
{
    /// <summary>
    /// Interaction logic  for BollingerUi.xaml
    /// Логика взаимодействия для BollingerUi.xaml
    /// </summary>
    public partial class BollingerWithSqueezeUi
    {
        /// <summary>
        /// indicator
        /// индикатор который мы настраиваем
        /// </summary>
        private BollingerWithSqueeze _bollingerWithSqueeze;

        /// <summary>
        /// whether indicator settings have been changed
        /// изменились ли настройки
        /// </summary>
        public bool IsChange;

        /// <summary>
        /// constructor
        /// конструктор
        /// </summary>
        /// <param name="bollingerWithSqueeze">configuration indicator/индикатор который мы будем настраивать</param>
        public BollingerWithSqueezeUi(BollingerWithSqueeze bollingerWithSqueeze)
        {
            InitializeComponent();
            _bollingerWithSqueeze = bollingerWithSqueeze;

            TextBoxSqueezeLenght.Text = _bollingerWithSqueeze.SqueezePeriod.ToString();
            TextBoxDeviation.Text = _bollingerWithSqueeze.Deviation.ToString();
            TextBoxLenght.Text = _bollingerWithSqueeze.Lenght.ToString();

            HostColorSqueeze.Child = new TextBox();
            HostColorSqueeze.Child.BackColor = _bollingerWithSqueeze.ColorSqueeze;
            HostColorUp.Child = new TextBox();
            HostColorUp.Child.BackColor = _bollingerWithSqueeze.ColorUp;
            HostColorDown.Child = new TextBox();
            HostColorDown.Child.BackColor = _bollingerWithSqueeze.ColorDown;
            HostColorSma.Child = new TextBox();
            HostColorSma.Child.BackColor = _bollingerWithSqueeze.ColorSma;

            CheckBoxPaintOnOff.IsChecked = _bollingerWithSqueeze.PaintOn;

            ButtonColorSqueeze.Content = OsLocalization.Charts.LabelButtonIndicatorColorSqueeze;
            ButtonColorUp.Content = OsLocalization.Charts.LabelButtonIndicatorColorUp;
            ButtonColorDown.Content = OsLocalization.Charts.LabelButtonIndicatorColorDown;
            CheckBoxPaintOnOff.Content = OsLocalization.Charts.LabelPaintIntdicatorIsVisible;
            ButtonAccept.Content = OsLocalization.Charts.LabelButtonIndicatorAccept;
            LabelIndicatorPeriod.Content = OsLocalization.Charts.LabelIndicatorPeriod;
            LabelIndicatorDeviation.Content = OsLocalization.Charts.LabelIndicatorDeviation;

        }

        /// <summary>
        /// accept button
        /// кнопка принять
        /// </summary>
        private void ButtonAccept_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Convert.ToInt32(TextBoxSqueezeLenght.Text) <= 0 || 
                    Convert.ToInt32(TextBoxLenght.Text) <= 0 || 
                    Convert.ToDecimal(TextBoxDeviation.Text) <= 0)
                {
                    throw new Exception("error");
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Процесс сохранения прерван. В одном из полей недопустимые значения");
                return;
            }

            _bollingerWithSqueeze.ColorSqueeze = HostColorSqueeze.Child.BackColor;
            _bollingerWithSqueeze.ColorUp = HostColorUp.Child.BackColor;
            _bollingerWithSqueeze.ColorDown = HostColorDown.Child.BackColor;
            _bollingerWithSqueeze.ColorSma = HostColorSma.Child.BackColor;
            _bollingerWithSqueeze.SqueezePeriod = Convert.ToInt32(TextBoxSqueezeLenght.Text);
            _bollingerWithSqueeze.Deviation = Convert.ToDecimal(TextBoxDeviation.Text);
            _bollingerWithSqueeze.Lenght = Convert.ToInt32(TextBoxLenght.Text);

            if (CheckBoxPaintOnOff.IsChecked.HasValue)
            {
                _bollingerWithSqueeze.PaintOn = CheckBoxPaintOnOff.IsChecked.Value;
            }
            
            _bollingerWithSqueeze.Save();
            IsChange = true;
            Close();
        }

        /// <summary>
        /// bollinger bands squeeze color button
        /// кнопка цвет сужения
        /// </summary>
        private void ButtonColorSqueeze_Click(object sender, RoutedEventArgs e)
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = HostColorSqueeze.Child.BackColor;
            dialog.ShowDialog();
            HostColorSqueeze.Child.BackColor = dialog.Color;
        }

        /// <summary>
        /// top line color button
        /// кнопка цвет верхней линии
        /// </summary>
        private void ButtonColorUp_Click(object sender, RoutedEventArgs e)
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = HostColorUp.Child.BackColor;
            dialog.ShowDialog();
            HostColorUp.Child.BackColor = dialog.Color;
        }

        /// <summary>
        /// bottom line color button
        /// кнопка цвет нижней линии
        /// </summary>
        private void ButtonColorDown_Click(object sender, RoutedEventArgs e)
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = HostColorDown.Child.BackColor;
            dialog.ShowDialog();
            HostColorDown.Child.BackColor = dialog.Color;
        }

        /// <summary>
        /// middle line color button
        /// кнопка цвет средней линии
        /// </summary>
        private void ButtonColorSma_Click(object sender, RoutedEventArgs e)
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = HostColorSma.Child.BackColor;
            dialog.ShowDialog();
            HostColorSma.Child.BackColor = dialog.Color;
        }
    }
}
