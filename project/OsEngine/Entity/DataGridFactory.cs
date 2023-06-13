/*
 * Your rights to use code governed by this license http://o-s-a.net/doc/license_simple_engine.pdf
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using OsEngine.Language;
using System.IO;

namespace OsEngine.Entity
{
    public class DataGridFactory
    {
        public static DataGridView GetDataGridView(DataGridViewSelectionMode selectionMode, DataGridViewAutoSizeRowsMode rowsSizeMode, bool createSaveMenu = false)
        {
            DataGridView grid = new DataGridView();

            grid.AllowUserToOrderColumns = true;
            grid.AllowUserToResizeRows = true;
            grid.AutoSizeRowsMode = rowsSizeMode;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToAddRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = selectionMode;
            grid.MultiSelect = false;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.ScrollBars = ScrollBars.None;
            grid.BackColor = Color.FromArgb(21, 26, 30);
            grid.BackgroundColor = Color.FromArgb(21, 26, 30);
           
            grid.GridColor = Color.FromArgb(17, 18, 23);
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.BorderStyle = BorderStyle.None;
            DataGridViewCellStyle style = new DataGridViewCellStyle();
            style.Alignment = DataGridViewContentAlignment.TopLeft;
            style.WrapMode = DataGridViewTriState.True;
            style.BackColor =  Color.FromArgb(21, 26, 30);
            style.SelectionBackColor = Color.FromArgb(17, 18, 23);
            style.ForeColor = Color.FromArgb(154, 156, 158);
          
            grid.DefaultCellStyle = style;
            grid.ColumnHeadersDefaultCellStyle = style;

            grid.MouseLeave += GridMouseLeaveEvent;

            grid.MouseWheel += GridMouseWheelEvent;

            if (createSaveMenu)
            {
                grid.Click += GridClickMenuEvent;
            }

            return grid;
        }

        private static void GridMouseWheelEvent(object sender, MouseEventArgs args)
        {
            DataGridView grid = (DataGridView)sender;

            if (grid.SelectedCells.Count == 0)
            {
                return;
            }
            int rowInd = grid.SelectedCells[0].RowIndex;
            if (args.Delta < 0)
            {
                rowInd++;
            }
            else if (args.Delta > 0)
            {
                rowInd--;
            }

            if (rowInd < 0)
            {
                rowInd = 0;
            }

            if (rowInd >= grid.Rows.Count)
            {
                rowInd = grid.Rows.Count - 1;
            }

            grid.Rows[rowInd].Selected = true;
            grid.Rows[rowInd].Cells[grid.SelectedCells[0].ColumnIndex].Selected = true;

            if (grid.FirstDisplayedScrollingRowIndex > rowInd)
            {
                grid.FirstDisplayedScrollingRowIndex = rowInd;
            }
        }

        private static void GridMouseLeaveEvent(Object sender, EventArgs e)
        {
            DataGridView grid = (DataGridView)sender;
            grid.EndEdit();
        }

        private static void GridClickMenuEvent(Object sender, EventArgs e)
        {
            DataGridView grid = (DataGridView)sender;

            List<MenuItem> items = new List<MenuItem>();

            items.Add(new MenuItem("Save table in file"));
            items.Add(new MenuItem("Do my calculations"));

            items[0].Click += delegate (Object sender, EventArgs e)
            {
                if (grid.Rows.Count == 0)
                {
                    return;
                }

                try
                {
                    SaveFileDialog myDialog = new SaveFileDialog();
                    myDialog.Filter = "*.txt|";
                    myDialog.ShowDialog();

                    if (string.IsNullOrEmpty(myDialog.FileName))
                    {
                        MessageBox.Show(OsLocalization.Journal.Message1);
                        return;
                    }

                    string fileName = myDialog.FileName;
                    if (fileName.Split('.').Length == 1)
                    {
                        fileName = fileName + ".txt";
                    }

                    string saveStr = "";

                    for (int i = 0; i < grid.Columns.Count; i++)
                    {
                        saveStr += grid.Columns[i].HeaderText + ",";
                    }

                    saveStr += "\r\n";

                    for (int i = 0; i < grid.Rows.Count; i++)
                    {
                        saveStr += grid.Rows[i].ToFormatString() + "\r\n";
                    }


                    StreamWriter writer = new StreamWriter(fileName);
                    writer.Write(saveStr);
                    writer.Close();
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.ToString());
                }
            };
            items[1].Click += delegate (Object sender, EventArgs e)
            {
                DoMyCalculations(grid);
            };

            ContextMenu menu = new ContextMenu(items.ToArray());

            MouseEventArgs mouse = (MouseEventArgs)e;
            if (mouse.Button != MouseButtons.Right)
            {
                return;
            }

            grid.ContextMenu = menu;
            grid.ContextMenu.Show(grid, new Point(mouse.X, mouse.Y));
        }

        private static void DoMyCalculations(DataGridView grid)
        {
            const decimal DEPOSIT = 1000.00m;
            const decimal MAX_ALLOWED_LEVERAGE = 3m;
            const decimal MAX_ALLOWED_DROWDAWN = 15m;
            const string START = "Start";
            const string END = "End";
            const string PROFIT = "Profit";
            const string PERIOD = "Period";
            const string OUT_OF_SAMPLE_PERIOD = "OutOfSample";

            if (grid.Rows.Count == 0)
            {
                return;
            }

            try
            {
                decimal tradedDays = 0;
                int startDateColumnIndex = -1;
                int endDateColumnIndex = -1;
                int periodColumnIndex = -1;
                int profitColumnIndex = -1;
                for (int i = 0; i < grid.Columns.Count; i++)
                {
                    if (grid.Columns[i].HeaderText == PERIOD)
                    {
                        periodColumnIndex = i;
                    }
                    if (grid.Columns[i].HeaderText == PROFIT)
                    {
                        profitColumnIndex = i;
                    }
                    if (grid.Columns[i].HeaderText == START)
                    {
                        startDateColumnIndex = i;
                    }
                    if (grid.Columns[i].HeaderText == END)
                    {
                        endDateColumnIndex = i;
                    }
                    if (periodColumnIndex >= 0 && profitColumnIndex >= 0 && startDateColumnIndex >= 0 && endDateColumnIndex >= 0)
                    {
                        break;
                    }
                }

                if (periodColumnIndex >= 0 && profitColumnIndex >= 0 && startDateColumnIndex >= 0 && endDateColumnIndex >= 0 && !String.IsNullOrWhiteSpace(grid.AccessibleDescription))
                {
                    List<decimal> leverages = new List<decimal>();
                    List<decimal> drowDawnsList = new List<decimal>();
                    List<decimal> outOfSampleProfitPercents = new List<decimal>();
                    foreach (string drowDawnStringValue in grid.AccessibleDescription.Split('|'))
                    {
                        if (!String.IsNullOrWhiteSpace(drowDawnStringValue))
                        {
                            drowDawnsList.Add(Decimal.Parse(drowDawnStringValue));
                        }
                    }

                    if (drowDawnsList.Count > 0)
                    {
                        for (int i = 0; i < grid.Rows.Count; i++)
                        {
                            string periodStringValue = grid.Rows[i].Cells[periodColumnIndex].FormattedValue.ToString();
                            if (periodStringValue == OUT_OF_SAMPLE_PERIOD)
                            {
                                DateTime startDate = DateTime.Parse(grid.Rows[i].Cells[startDateColumnIndex].FormattedValue.ToString());
                                DateTime endDate = DateTime.Parse(grid.Rows[i].Cells[endDateColumnIndex].FormattedValue.ToString());
                                int outOfSampleSizeInDays = (int)(endDate - startDate).TotalDays;
                                tradedDays += outOfSampleSizeInDays;
                                string profitStringValue = grid.Rows[i].Cells[profitColumnIndex].FormattedValue.ToString();
                                profitStringValue = profitStringValue.Replace(".", System.Threading.Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator);
                                decimal profitMoney = Decimal.Parse(profitStringValue);
                                decimal profitPercentNoLeverage = profitMoney * 100 / DEPOSIT;
                                decimal drowDawn = drowDawnsList[outOfSampleProfitPercents.Count];
                                decimal leverage = drowDawn != 0 ? MAX_ALLOWED_DROWDAWN / Math.Abs(drowDawn) : MAX_ALLOWED_LEVERAGE;
                                leverage = leverage > MAX_ALLOWED_LEVERAGE ? MAX_ALLOWED_LEVERAGE : leverage;
                                leverages.Add(leverage);
                                outOfSampleProfitPercents.Add(profitPercentNoLeverage * leverage);
                            }
                        }
                    }

                    if (outOfSampleProfitPercents.Count > 0 && outOfSampleProfitPercents.Count == drowDawnsList.Count)
                    {
                        string resultReport = String.Format("Deposit = {0}$\n", DEPOSIT);
                        resultReport += String.Format("Max allowed DD = -{0}%\n", MAX_ALLOWED_DROWDAWN);
                        resultReport += String.Format("Max allowed LEVERAGE = {0}\n\n", MAX_ALLOWED_LEVERAGE);

                        decimal resultDeposit = DEPOSIT;
                        for (int i = 0; i < outOfSampleProfitPercents.Count; i++)
                        {
                            decimal profitPercent = outOfSampleProfitPercents[i];
                            decimal profit = resultDeposit * profitPercent / 100;
                            string message = String.Format("{0} => {1}${2}{3}$[{4}%]", i < 9 ? "0" + (i + 1).ToString() : (i + 1).ToString(), Math.Round(resultDeposit, 2), profit > 0 ? " + " : " ", Math.Round(profit, 2), Math.Round(profitPercent, 2));
                            message = message.Replace(" -", " - ");
                            resultDeposit += profit;
                            message += String.Format(" = {0}$", Math.Round(resultDeposit, 2));
                            message += String.Format("\t | \tInSampleDD = {0}%", Math.Round(drowDawnsList[i], 2));
                            message += String.Format("\t | \tLeverage = {0}", Math.Round(leverages[i], 2));
                            resultReport += message + Environment.NewLine;
                        }

                        decimal totalProfitPercent = Math.Round(resultDeposit * 100 / DEPOSIT - 100, 2);
                        decimal totalProfitMoney = Math.Round(resultDeposit - DEPOSIT, 2);
                        decimal thirtyDaysProfitPercent = totalProfitPercent / tradedDays * 30m;
                        decimal thirtyDaysProfitMoney = totalProfitMoney / tradedDays * 30m;
                        resultReport += totalProfitMoney > 0 ? "\n*** PROFIT! ***" : "\n*** LOSS! ***";
                        resultReport += String.Format("\n30 days = {0}{1}% [ {2}{3}$ ]", thirtyDaysProfitPercent > 0 ? "+" : "", Math.Round(thirtyDaysProfitPercent, 2), thirtyDaysProfitMoney > 0 ? "+" : "", Math.Round(thirtyDaysProfitMoney, 2));
                        resultReport += String.Format("\nTOTAL = {0}{1}% [ {2}{3}$ ] : {4} months = {5} days", totalProfitPercent > 0 ? "+" : "", totalProfitPercent, totalProfitMoney > 0 ? "+" : "", totalProfitMoney, Math.Round(tradedDays / 30m, 1), tradedDays);
                        resultReport += String.Format("\nAVG_InSampleDD = {0}%", Math.Round(drowDawnsList.Sum() / drowDawnsList.Count, 2));
                        resultReport += String.Format("\nAVG_Leverage = {0}", Math.Round(leverages.Sum() / leverages.Count, 2));

                        MessageBox.Show(resultReport);
                    }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
            }
        }

        public static void ClearLink(DataGridView grid)
        {
            grid.MouseLeave -= GridMouseLeaveEvent;
            grid.MouseWheel -= GridMouseWheelEvent;
            grid.Click -= GridClickMenuEvent;
        }

        public static DataGridView GetDataGridPosition(bool readOnly = true)
        {
            DataGridView newGrid = GetDataGridView(DataGridViewSelectionMode.FullRowSelect,
                DataGridViewAutoSizeRowsMode.AllCells);
            newGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            DataGridViewCellStyle style = newGrid.DefaultCellStyle;

            DataGridViewTextBoxCell cell0 = new DataGridViewTextBoxCell();
            cell0.Style = style;

            DataGridViewColumn colum0 = new DataGridViewColumn();
            colum0.CellTemplate = cell0;
            colum0.HeaderText = OsLocalization.Entity.PositionColumn1;
            colum0.ReadOnly = true;
            colum0.Width = 50;
            newGrid.Columns.Add(colum0);

            DataGridViewColumn colum01 = new DataGridViewColumn();
            colum01.CellTemplate = cell0;
            colum01.HeaderText = OsLocalization.Entity.PositionColumn2;
            colum01.ReadOnly = true;
            colum01.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            newGrid.Columns.Add(colum01);

            DataGridViewColumn colum02 = new DataGridViewColumn();
            colum02.CellTemplate = cell0;
            colum02.HeaderText = OsLocalization.Entity.PositionColumn3;
            colum02.ReadOnly = true;
            colum02.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            newGrid.Columns.Add(colum02);

            DataGridViewColumn colu = new DataGridViewColumn();
            colu.CellTemplate = cell0;
            colu.HeaderText = OsLocalization.Entity.PositionColumn4;
            colu.ReadOnly = readOnly;
            colu.Width = 70;

            newGrid.Columns.Add(colu);

            DataGridViewColumn colum1 = new DataGridViewColumn();
            colum1.CellTemplate = cell0;
            colum1.HeaderText = OsLocalization.Entity.PositionColumn5;
            colum1.ReadOnly = readOnly;
            colum1.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            newGrid.Columns.Add(colum1);

            // position SIDE
            if(readOnly == true)
            {
                
                DataGridViewColumn colum2 = new DataGridViewColumn();
                colum2.CellTemplate = cell0;
                colum2.HeaderText = OsLocalization.Entity.PositionColumn6;
                colum2.ReadOnly = readOnly;
                colum2.Width = 60;
                newGrid.Columns.Add(colum2);
            }
            else
            {
                DataGridViewComboBoxColumn dirColumn = new DataGridViewComboBoxColumn();
                dirColumn.HeaderText = OsLocalization.Entity.PositionColumn6;
                dirColumn.ReadOnly = readOnly;
                dirColumn.Width = 60;
                newGrid.Columns.Add(dirColumn);
            }

            // position STATE
            if (readOnly == true)
            {
                DataGridViewColumn colum3 = new DataGridViewColumn();
                colum3.CellTemplate = cell0;
                colum3.HeaderText = OsLocalization.Entity.PositionColumn7;
                colum3.ReadOnly = readOnly;
                colum3.Width = 100;
                newGrid.Columns.Add(colum3);
            }
            else
            {
                DataGridViewComboBoxColumn stateColumn = new DataGridViewComboBoxColumn();
                stateColumn.HeaderText = OsLocalization.Entity.PositionColumn7;
                stateColumn.ReadOnly = readOnly;
                stateColumn.Width = 100;
                newGrid.Columns.Add(stateColumn);
            }

            DataGridViewColumn colum4 = new DataGridViewColumn();
            colum4.CellTemplate = cell0;
            colum4.HeaderText = OsLocalization.Entity.PositionColumn8;
            colum4.ReadOnly = true;
            colum4.Width = 60;

            newGrid.Columns.Add(colum4);

            DataGridViewColumn colum45 = new DataGridViewColumn();
            colum45.CellTemplate = cell0;
            colum45.HeaderText = OsLocalization.Entity.PositionColumn9;
            colum45.ReadOnly = true;
            colum45.Width = 60;

            newGrid.Columns.Add(colum45);

            DataGridViewColumn colum5 = new DataGridViewColumn();
            colum5.CellTemplate = cell0;
            colum5.HeaderText = OsLocalization.Entity.PositionColumn10;
            colum5.ReadOnly = true;
            colum5.Width = 60;

            newGrid.Columns.Add(colum5);

            DataGridViewColumn colum6 = new DataGridViewColumn();
            colum6.CellTemplate = cell0;
            colum6.HeaderText = OsLocalization.Entity.PositionColumn11;
            colum6.ReadOnly = true;
            colum6.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            newGrid.Columns.Add(colum6);

            DataGridViewColumn colum61 = new DataGridViewColumn();
            colum61.CellTemplate = cell0;
            colum61.HeaderText = OsLocalization.Entity.PositionColumn12;
            colum61.ReadOnly = true;
            colum61.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            newGrid.Columns.Add(colum61);

            DataGridViewColumn colum8 = new DataGridViewColumn();
            colum8.CellTemplate = cell0;
            colum8.HeaderText = OsLocalization.Entity.PositionColumn13;
            colum8.ReadOnly = true;
            colum8.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            newGrid.Columns.Add(colum8);

            DataGridViewColumn colum9 = new DataGridViewColumn();
            colum9.CellTemplate = cell0;
            colum9.HeaderText = OsLocalization.Entity.PositionColumn14;
            colum9.ReadOnly = readOnly;
            colum9.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            newGrid.Columns.Add(colum9);

            DataGridViewColumn colum10 = new DataGridViewColumn();
            colum10.CellTemplate = cell0;
            colum10.HeaderText = OsLocalization.Entity.PositionColumn15;
            colum10.ReadOnly = readOnly;
            colum10.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            newGrid.Columns.Add(colum10);

            DataGridViewColumn colum11 = new DataGridViewColumn();
            colum11.CellTemplate = cell0;
            colum11.HeaderText = OsLocalization.Entity.PositionColumn16;
            colum11.ReadOnly = readOnly;
            colum11.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            newGrid.Columns.Add(colum11);

            DataGridViewColumn colum12 = new DataGridViewColumn();
            colum12.CellTemplate = cell0;
            colum12.HeaderText = OsLocalization.Entity.PositionColumn17;
            colum12.ReadOnly = readOnly;
            colum12.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            newGrid.Columns.Add(colum12);

            DataGridViewColumn colum13 = new DataGridViewColumn();
            colum13.CellTemplate = cell0;
            colum13.HeaderText = OsLocalization.Entity.PositionColumn18;
            colum13.ReadOnly = readOnly;
            colum13.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            newGrid.Columns.Add(colum13);

            DataGridViewColumn colum14 = new DataGridViewColumn();
            colum14.CellTemplate = cell0;
            colum14.HeaderText = OsLocalization.Entity.PositionColumn19;
            colum14.ReadOnly = readOnly;
            colum14.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colum14.Width = 60;
            newGrid.Columns.Add(colum14);

            return newGrid;
        }

        public static DataGridView GetDataGridOrder(bool readOnly = true)
        {
            DataGridView newGrid = GetDataGridView(DataGridViewSelectionMode.FullRowSelect,
                DataGridViewAutoSizeRowsMode.AllCells);

            newGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            DataGridViewTextBoxCell cell0 = new DataGridViewTextBoxCell();
            cell0.Style = newGrid.DefaultCellStyle;

            // User ID
            DataGridViewColumn colum0 = new DataGridViewColumn();
            colum0.CellTemplate = cell0;
            colum0.HeaderText = OsLocalization.Entity.OrderColumn1;
            colum0.ReadOnly = true;
            colum0.Width = 50;
            newGrid.Columns.Add(colum0);

            // Market ID

            DataGridViewColumn colum01 = new DataGridViewColumn();
            colum01.CellTemplate = cell0;
            colum01.HeaderText = OsLocalization.Entity.OrderColumn2;
            colum01.ReadOnly = readOnly;
            colum01.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            newGrid.Columns.Add(colum01);

            // Time Create
            if(readOnly)
            {
                DataGridViewColumn colum02 = new DataGridViewColumn();
                colum02.CellTemplate = cell0;
                colum02.HeaderText = OsLocalization.Entity.OrderColumn3;
                colum02.ReadOnly = true;
                colum02.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                newGrid.Columns.Add(colum02);
            }
            else
            {
                DataGridViewButtonColumn colum02 = new DataGridViewButtonColumn();
                colum02.HeaderText = OsLocalization.Entity.OrderColumn3;
                colum02.ReadOnly = true;
                colum02.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                newGrid.Columns.Add(colum02);
            }

            // Security
            DataGridViewColumn colu = new DataGridViewColumn();
            colu.CellTemplate = cell0;
            colu.HeaderText = OsLocalization.Entity.OrderColumn4;
            colu.ReadOnly = true;
            colu.Width = 60;
            newGrid.Columns.Add(colu);

            // Portfolio
            DataGridViewColumn colum1 = new DataGridViewColumn();
            colum1.CellTemplate = cell0;
            colum1.HeaderText = OsLocalization.Entity.OrderColumn5;
            colum1.ReadOnly = readOnly;
            colum1.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            newGrid.Columns.Add(colum1);

            // Direction
            if(readOnly)
            {
                DataGridViewColumn colum2 = new DataGridViewColumn();
                colum2.CellTemplate = cell0;
                colum2.HeaderText = OsLocalization.Entity.OrderColumn6;
                colum2.ReadOnly = true;
                colum2.Width = 40;
                newGrid.Columns.Add(colum2);
            }
            else
            {
                DataGridViewComboBoxColumn dirColumn = new DataGridViewComboBoxColumn();
                dirColumn.HeaderText = OsLocalization.Entity.OrderColumn6;
                dirColumn.ReadOnly = readOnly;
                dirColumn.Width = 60;
                newGrid.Columns.Add(dirColumn);
            }

            // State
            if (readOnly)
            {
                DataGridViewColumn colum3 = new DataGridViewColumn();
                colum3.CellTemplate = cell0;
                colum3.HeaderText = OsLocalization.Entity.OrderColumn7;
                colum3.ReadOnly = true;
                colum3.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                newGrid.Columns.Add(colum3);
            }
            else
            {
                DataGridViewComboBoxColumn stateColumn = new DataGridViewComboBoxColumn();
                stateColumn.HeaderText = OsLocalization.Entity.OrderColumn7;
                stateColumn.ReadOnly = readOnly;
                stateColumn.Width = 100;
                newGrid.Columns.Add(stateColumn);
            }

            // Price
            DataGridViewColumn colum4 = new DataGridViewColumn();
            colum4.CellTemplate = cell0;
            colum4.HeaderText = OsLocalization.Entity.OrderColumn8;
            colum4.ReadOnly = readOnly;
            colum4.Width = 60;
            newGrid.Columns.Add(colum4);

            // Execution price
            DataGridViewColumn colum45 = new DataGridViewColumn();
            colum45.CellTemplate = cell0;
            colum45.HeaderText = OsLocalization.Entity.OrderColumn9;
            colum45.ReadOnly = true;
            colum45.Width = 60;
            newGrid.Columns.Add(colum45);

            // Volume
            DataGridViewColumn colum5 = new DataGridViewColumn();
            colum5.CellTemplate = cell0;
            colum5.HeaderText = OsLocalization.Entity.OrderColumn10;
            colum5.ReadOnly = readOnly;
            colum5.Width = 60;
            newGrid.Columns.Add(colum5);

            // Type
            if(readOnly)
            {
                DataGridViewColumn colum6 = new DataGridViewColumn();
                colum6.CellTemplate = cell0;
                colum6.HeaderText = OsLocalization.Entity.OrderColumn11;
                colum6.ReadOnly = true;
                colum6.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                newGrid.Columns.Add(colum6);
            }
            else
            {
                DataGridViewComboBoxColumn typeColumn = new DataGridViewComboBoxColumn();
                typeColumn.HeaderText = OsLocalization.Entity.OrderColumn11;
                typeColumn.ReadOnly = readOnly;
                typeColumn.Width = 70;
                newGrid.Columns.Add(typeColumn);
            }

            // RoundTrip
            DataGridViewColumn colum7 = new DataGridViewColumn();
            colum7.CellTemplate = cell0;
            colum7.HeaderText = OsLocalization.Entity.OrderColumn12;
            colum7.ReadOnly = true;
            colum7.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            newGrid.Columns.Add(colum7);

            return newGrid;
        }

        public static DataGridView GetDataGridMyTrade(bool readOnly = true)
        {
            DataGridView newGrid = GetDataGridView(DataGridViewSelectionMode.FullRowSelect,
                DataGridViewAutoSizeRowsMode.AllCells);
            newGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            DataGridViewTextBoxCell cell0 = new DataGridViewTextBoxCell();
            cell0.Style = newGrid.DefaultCellStyle;

            // 0 Id

            DataGridViewColumn colum0 = new DataGridViewColumn();
            colum0.CellTemplate = cell0;
            colum0.HeaderText = OsLocalization.Entity.TradeColumn1;
            colum0.ReadOnly = true;
            colum0.Width = 50;
            newGrid.Columns.Add(colum0);

            // 1 Order Id

            DataGridViewColumn colum03 = new DataGridViewColumn();
            colum03.CellTemplate = cell0;
            colum03.HeaderText = OsLocalization.Entity.TradeColumn2;
            colum03.ReadOnly = true;
            colum03.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            newGrid.Columns.Add(colum03);

            // 2 Security

            DataGridViewColumn colum01 = new DataGridViewColumn();
            colum01.CellTemplate = cell0;
            colum01.HeaderText = OsLocalization.Entity.TradeColumn3;
            colum01.ReadOnly = true;
            colum01.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            newGrid.Columns.Add(colum01);

            // 3 Time
            if (readOnly)
            {
                DataGridViewColumn colum02 = new DataGridViewColumn();
                colum02.CellTemplate = cell0;
                colum02.HeaderText = OsLocalization.Entity.TradeColumn4;
                colum02.ReadOnly = true;
                colum02.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                newGrid.Columns.Add(colum02);
            }
            else
            {
                DataGridViewButtonColumn colum02 = new DataGridViewButtonColumn();
                colum02.HeaderText = OsLocalization.Entity.TradeColumn4;
                colum02.ReadOnly = true;
                colum02.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                newGrid.Columns.Add(colum02);
            }

            // 4 Price

            DataGridViewColumn colu = new DataGridViewColumn();
            colu.CellTemplate = cell0;
            colu.HeaderText = OsLocalization.Entity.TradeColumn5;
            colu.ReadOnly = readOnly;
            colu.Width = 60;
            newGrid.Columns.Add(colu);

            // 5 Volume

            DataGridViewColumn colum1 = new DataGridViewColumn();
            colum1.CellTemplate = cell0;
            colum1.HeaderText = OsLocalization.Entity.TradeColumn6;
            colum1.ReadOnly = readOnly;
            colum1.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            newGrid.Columns.Add(colum1);

            // 6 Direction

            if (readOnly)
            {
                DataGridViewColumn colum2 = new DataGridViewColumn();
                colum2.CellTemplate = cell0;
                colum2.HeaderText = OsLocalization.Entity.TradeColumn7;
                colum2.ReadOnly = true;
                colum2.Width = 40;
                newGrid.Columns.Add(colum2);
            }
            else
            {
                DataGridViewComboBoxColumn dirColumn = new DataGridViewComboBoxColumn();
                dirColumn.HeaderText = OsLocalization.Entity.TradeColumn7;
                dirColumn.ReadOnly = readOnly;
                dirColumn.Width = 60;
                newGrid.Columns.Add(dirColumn);
            }

            return newGrid;
        }

        public static DataGridView GetDataGridProxies()
        {
            DataGridView newGrid = DataGridFactory.GetDataGridView(DataGridViewSelectionMode.FullRowSelect,
                DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders);

            DataGridViewTextBoxCell cell0 = new DataGridViewTextBoxCell();
            cell0.Style = newGrid.DefaultCellStyle;

            DataGridViewColumn column0 = new DataGridViewColumn();
            column0.CellTemplate = cell0;
            column0.HeaderText = OsLocalization.Entity.ProxiesColumn1;
            column0.ReadOnly = true;
            column0.Width = 170;

            newGrid.Columns.Add(column0);

            DataGridViewColumn column1 = new DataGridViewColumn();
            column1.CellTemplate = cell0;
            column1.HeaderText = OsLocalization.Entity.ProxiesColumn2;
            column1.ReadOnly = true;
            column1.Width = 100;

            newGrid.Columns.Add(column1);

            DataGridViewColumn column = new DataGridViewColumn();
            column.CellTemplate = cell0;
            column.HeaderText = OsLocalization.Entity.ProxiesColumn3;
            column.ReadOnly = true;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            newGrid.Columns.Add(column);

            return newGrid;
        }

        public static DataGridView GetDataGridSecurities()
        {
            DataGridView grid = GetDataGridView(DataGridViewSelectionMode.FullRowSelect,
                DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders);

            DataGridViewTextBoxCell cell0 = new DataGridViewTextBoxCell();
            cell0.Style = grid.DefaultCellStyle;

            DataGridViewColumn column0 = new DataGridViewColumn();
            column0.CellTemplate = cell0;
            column0.HeaderText = OsLocalization.Entity.SecuritiesColumn1;
            column0.ReadOnly = true;
            column0.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            grid.Columns.Add(column0);

            DataGridViewColumn column = new DataGridViewColumn();
            column.CellTemplate = cell0;
            column.HeaderText = OsLocalization.Entity.SecuritiesColumn2;
            column.ReadOnly = true;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            grid.Columns.Add(column);

            DataGridViewColumn column1 = new DataGridViewColumn();
            column1.CellTemplate = cell0;
            column1.HeaderText = OsLocalization.Entity.SecuritiesColumn3;
            column1.ReadOnly = false;
            column1.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            grid.Columns.Add(column1);

            DataGridViewColumn column3 = new DataGridViewColumn();
            column3.CellTemplate = cell0;
            column3.HeaderText = OsLocalization.Entity.SecuritiesColumn4;
            column3.ReadOnly = false;
            column3.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            grid.Columns.Add(column3);

            DataGridViewColumn column4 = new DataGridViewColumn();
            column4.CellTemplate = cell0;
            column4.HeaderText = OsLocalization.Entity.SecuritiesColumn5;
            column4.ReadOnly = false;
            column4.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            grid.Columns.Add(column4);

            DataGridViewColumn column8 = new DataGridViewColumn();
            column8.CellTemplate = cell0;
            column8.HeaderText = "";
            column8.ReadOnly = true;
            column8.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            grid.Columns.Add(column8);

            return grid;
        }

        public static DataGridView GetDataGridPortfolios()
        {
            DataGridView _gridPosition = DataGridFactory.GetDataGridView(DataGridViewSelectionMode.FullRowSelect, DataGridViewAutoSizeRowsMode.None);

            DataGridViewTextBoxCell cell0 = new DataGridViewTextBoxCell();
            cell0.Style = _gridPosition.DefaultCellStyle;

            DataGridViewColumn column0 = new DataGridViewColumn();
            column0.CellTemplate = cell0;
            column0.HeaderText = OsLocalization.Entity.ColumnPortfolio1;
            column0.ReadOnly = true;
            column0.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            _gridPosition.Columns.Add(column0);

            DataGridViewColumn column = new DataGridViewColumn();
            column.CellTemplate = cell0;
            column.HeaderText = OsLocalization.Entity.ColumnPortfolio2;
            column.ReadOnly = true;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            _gridPosition.Columns.Add(column);

            DataGridViewColumn column1 = new DataGridViewColumn();
            column1.CellTemplate = cell0;
            column1.HeaderText = OsLocalization.Entity.ColumnPortfolio3;
            column1.ReadOnly = true;
            column1.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            _gridPosition.Columns.Add(column1);

            DataGridViewColumn column3 = new DataGridViewColumn();
            column3.CellTemplate = cell0;
            column3.HeaderText = OsLocalization.Entity.ColumnPortfolio4;
            column3.ReadOnly = true;
            column3.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            _gridPosition.Columns.Add(column3);

            DataGridViewColumn column4 = new DataGridViewColumn();
            column4.CellTemplate = cell0;
            column4.HeaderText = OsLocalization.Entity.ColumnPortfolio5;
            column4.ReadOnly = true;
            column4.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            _gridPosition.Columns.Add(column4);

            DataGridViewColumn column5 = new DataGridViewColumn();
            column5.CellTemplate = cell0;
            column5.HeaderText = OsLocalization.Entity.ColumnPortfolio6;
            column5.ReadOnly = true;
            column5.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            _gridPosition.Columns.Add(column5);

            DataGridViewColumn column6 = new DataGridViewColumn();
            column6.CellTemplate = cell0;
            column6.HeaderText = OsLocalization.Entity.ColumnPortfolio7;
            column6.ReadOnly = true;
            column6.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            _gridPosition.Columns.Add(column6);

            DataGridViewColumn column7 = new DataGridViewColumn();
            column7.CellTemplate = cell0;
            column7.HeaderText = OsLocalization.Entity.ColumnPortfolio8;
            column7.ReadOnly = true;
            column7.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _gridPosition.Columns.Add(column7);

            return _gridPosition;
        }

        public static DataGridView GetDataGridServers()
        {
            DataGridView newGrid = DataGridFactory.GetDataGridView(DataGridViewSelectionMode.FullRowSelect, DataGridViewAutoSizeRowsMode.None);

            DataGridViewTextBoxCell cell0 = new DataGridViewTextBoxCell();
            cell0.Style = newGrid.DefaultCellStyle;

            DataGridViewColumn colum0 = new DataGridViewColumn();
            colum0.CellTemplate = cell0;
            colum0.HeaderText = OsLocalization.Entity.ColumnServers1;
            colum0.ReadOnly = true;
            colum0.Width = 150;
            newGrid.Columns.Add(colum0);

            DataGridViewColumn colu = new DataGridViewColumn();
            colu.CellTemplate = cell0;
            colu.HeaderText = OsLocalization.Entity.ColumnServers2;
            colu.ReadOnly = true;
            colu.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            newGrid.Columns.Add(colu);

            return newGrid;
        }

        public static DataGridView GetDataGridDataSource()
        {
            DataGridView myGridView = DataGridFactory.GetDataGridView(DataGridViewSelectionMode.FullRowSelect, DataGridViewAutoSizeRowsMode.None);

            DataGridViewTextBoxCell cell0 = new DataGridViewTextBoxCell();
            cell0.Style = myGridView.DefaultCellStyle;

            DataGridViewColumn column2 = new DataGridViewColumn();
            column2.CellTemplate = cell0;
            column2.HeaderText = OsLocalization.Entity.ColumnDataSource1;
            column2.ReadOnly = true;
            column2.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            myGridView.Columns.Add(column2);

            DataGridViewColumn column0 = new DataGridViewColumn();
            column0.CellTemplate = cell0;
            column0.HeaderText = OsLocalization.Entity.ColumnDataSource2;
            column0.ReadOnly = true;
            column0.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            myGridView.Columns.Add(column0);

            DataGridViewColumn column = new DataGridViewColumn();
            column.CellTemplate = cell0;
            column.HeaderText = OsLocalization.Entity.ColumnDataSource3;
            column.ReadOnly = false;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            myGridView.Columns.Add(column);

            DataGridViewColumn column1 = new DataGridViewColumn();
            column1.CellTemplate = cell0;
            column1.HeaderText = OsLocalization.Entity.ColumnDataSource4;
            column1.ReadOnly = true;
            column1.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            myGridView.Columns.Add(column1);

            DataGridViewColumn column3 = new DataGridViewColumn();
            column3.CellTemplate = cell0;
            column3.HeaderText = OsLocalization.Entity.ColumnDataSource5;
            column3.ReadOnly = true;
            column3.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            myGridView.Columns.Add(column3);

            DataGridViewColumn column4 = new DataGridViewColumn();
            column4.CellTemplate = cell0;
            column4.HeaderText = OsLocalization.Entity.ColumnDataSource6;
            column4.ReadOnly = true;
            column4.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            myGridView.Columns.Add(column4);

            return myGridView;
        }

    }
}