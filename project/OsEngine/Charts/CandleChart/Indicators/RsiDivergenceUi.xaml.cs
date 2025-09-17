using System;
using System.Windows;
using System.Windows.Forms;
using OsEngine.Language;
using MessageBox = System.Windows.MessageBox;

namespace OsEngine.Charts.CandleChart.Indicators
{
    /// <summary>
    /// Interaction logic for RsiDivergenceUi.xaml
    /// Логика взаимодействия для RsiDivergenceUi.xaml
    /// </summary>
    public partial class RsiDivergenceUi
    {
        /// <summary>
        /// indicator that we're setting up
        /// индикатор который мы настраиваем
        /// </summary>
        private RsiDivergence _rsiDivergence;

        /// <summary>
        /// whether indicator settings have been changed
        /// изменялись ли настройки
        /// </summary>
        public bool IsChange;

        /// <summary>
        /// constructor
        /// конструктор
        /// </summary>
        /// <param name="rsi">configuration indicator/индикатор который будем настраивать</param>
        public RsiDivergenceUi(RsiDivergence rsiDivergence)
        {
            InitializeComponent();
            _rsiDivergence = rsiDivergence;

            TextBoxLength.Text = _rsiDivergence.Length.ToString();
            HostColor.Child = new TextBox();
            HostColor.Child.BackColor = _rsiDivergence.ColorBase;

            ButtonColor.Content = OsLocalization.Charts.LabelButtonIndicatorColor;
            ButtonAccept.Content = OsLocalization.Charts.LabelButtonIndicatorAccept;
            LabelIndicatorPeriod.Content = OsLocalization.Charts.LabelIndicatorPeriod;
        }

        /// <summary>
        /// accept button
        /// кнопка принять
        /// </summary>
        private void ButtonAccept_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Convert.ToInt32(TextBoxLength.Text) <= 0)
                {
                    throw new Exception("error");
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Процесс сохранения прерван. В одном из полей недопустимые значения");
                return;
            }

            _rsiDivergence.ColorBase = HostColor.Child.BackColor;
            _rsiDivergence.Length = Convert.ToInt32(TextBoxLength.Text);

            _rsiDivergence.Save();
            IsChange = true;
            Close();
        }

        /// <summary>
        /// color setting button
        /// кнопка далее выбор цвета
        /// </summary>
        private void ButtonColor_Click(object sender, RoutedEventArgs e)
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = HostColor.Child.BackColor;
            dialog.ShowDialog();

            HostColor.Child.BackColor = dialog.Color;
        }
    }
}
