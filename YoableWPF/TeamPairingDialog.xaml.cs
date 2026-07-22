using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using YoableWPF.Models;

namespace YoableWPF
{
    /// <summary>
    /// One body-class/head-class pairing row backed by combo box selections.
    /// </summary>
    public class TeamPairRow
    {
        public List<LabelClass> Classes { get; set; }
        public LabelClass SelectedBody { get; set; }
        public LabelClass SelectedHead { get; set; }
    }

    /// <summary>
    /// Edits the body/head class pairings a project uses for ClassTransfer team consistency.
    /// </summary>
    public partial class TeamPairingDialog : Window
    {
        private readonly List<LabelClass> projectClasses;
        public ObservableCollection<TeamPairRow> Rows { get; }

        /// <summary>
        /// Fully specified, de-duplicated pairings; only meaningful once the dialog returns true.
        /// </summary>
        public List<ClassTeamPair> Pairs => Rows
            .Where(row => row.SelectedBody != null && row.SelectedHead != null &&
                          row.SelectedBody.ClassId != row.SelectedHead.ClassId)
            .Select(row => new ClassTeamPair
            {
                BodyClassId = row.SelectedBody.ClassId,
                HeadClassId = row.SelectedHead.ClassId
            })
            .GroupBy(pair => (pair.BodyClassId, pair.HeadClassId))
            .Select(group => group.First())
            .ToList();

        public TeamPairingDialog(
            IEnumerable<LabelClass> classes,
            IEnumerable<ClassTeamPair> savedPairs)
        {
            projectClasses = classes?.ToList() ?? new List<LabelClass>();
            Rows = new ObservableCollection<TeamPairRow>();

            InitializeComponent();

            if (savedPairs != null)
            {
                foreach (ClassTeamPair pair in savedPairs)
                {
                    Rows.Add(new TeamPairRow
                    {
                        Classes = projectClasses,
                        SelectedBody = projectClasses.FirstOrDefault(c => c.ClassId == pair.BodyClassId),
                        SelectedHead = projectClasses.FirstOrDefault(c => c.ClassId == pair.HeadClassId)
                    });
                }
            }

            PairList.ItemsSource = Rows;
        }

        private void AddPair_Click(object sender, RoutedEventArgs e)
        {
            Rows.Add(new TeamPairRow { Classes = projectClasses });
        }

        private void RemovePair_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TeamPairRow row)
                Rows.Remove(row);
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
