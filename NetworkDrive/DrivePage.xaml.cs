using Microsoft.Win32;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NetworkDrive
{
    /// <summary>
    /// Interaction logic for Drive.xaml
    /// </summary>
    public partial class DrivePage : Page
    {
        #region DPs.

        public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register("StatusText", typeof(string), typeof(DrivePage));
        public string StatusText
        {
            get { return (string)GetValue(StatusTextProperty); }
            set { SetValue(StatusTextProperty, value); }
        }

        public static readonly DependencyProperty IsBlobSelectedProperty = DependencyProperty.Register("IsBlobSelected", typeof(bool), typeof(DrivePage));
        public bool IsBlobSelected
        {
            get { return (bool)GetValue(IsBlobSelectedProperty); }
            set { SetValue(IsBlobSelectedProperty, value); }
        }

        public static readonly DependencyProperty IsDirectorySelectedProperty = DependencyProperty.Register("IsDirectorySelected", typeof(bool), typeof(DrivePage));
        public bool IsDirectorySelected
        {
            get { return (bool)GetValue(IsDirectorySelectedProperty); }
            set { SetValue(IsDirectorySelectedProperty, value); }
        }

        #endregion

        public DrivePage()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        protected override async void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            await UpdateView();
        }

        #region Blob operations.

        /// <summary>
        /// Handle button to download blob.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var item = View.SelectedItem as TreeViewItem;
            var blob = item.Tag as CloudBlockBlob;
            var name = item.Header as string;

            var saveFileDialog = new SaveFileDialog
            {
                FileName = name,
                Title = "Download..."
            };

            if (saveFileDialog.ShowDialog() != true)
                return;

            StatusText = "Downloading...";
            await blob.DownloadToFileAsync(saveFileDialog.FileName, FileMode.Create);
            StatusText = "Success!";
        }

        /// <summary>
        /// Handle button to upload into a directory.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var item = View.SelectedItem as TreeViewItem;

            var (containerName, directoryName) = GetContainerAndDirectory(item);

            var client = Helpers.Storage.CreateCloudBlobClient();
            var container = client.GetContainerReference(containerName);

            var openFileDialog = new OpenFileDialog
            {
                Title = "Upload..."
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            var filePath = openFileDialog.FileName;
            var fileName = filePath.Split(System.IO.Path.DirectorySeparatorChar).Last();

            var blobReference = container.GetBlockBlobReference($"{directoryName}{fileName}");

            StatusText = "Uploading...";
            await blobReference.UploadFromFileAsync(filePath);
            await UpdateView();
            StatusText = "Success!";
        }

        /// <summary>
        /// Handle button to delete a blob.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var item = View.SelectedItem as TreeViewItem;
            var blob = item.Tag as CloudBlockBlob;
            var name = item.Header as string;

            StatusText = "Deleting...";
            await blob.DeleteAsync();
            await UpdateView();
            StatusText = "Success!";
        }

        #endregion

        #region TreeView Helpers.

        /// <summary>
        /// Refreshes the TreeView.
        /// </summary>
        private async Task UpdateView()
        {
            StatusText = "Loading...";

            await Dispatcher.InvokeAsync(() =>
            {
                var client = Helpers.Storage.CreateCloudBlobClient();

                View.Items.Clear();
                foreach (var container in client.ListContainers())
                    View.Items.Add(CreateRootTreeViewItem(container));
            });

            StatusText = "Success!";
        }

        /// <summary>
        /// Create a root TreeViewItem for a Storage Container.
        /// </summary>
        /// <param name="container"></param>
        /// <returns>The root TreeViewItem for the container.</returns>
        private TreeViewItem CreateRootTreeViewItem(CloudBlobContainer container)
        {
            var item = new TreeViewItem
            {
                Header = container.Name,
                Name = container.Name
            };

            var blobs = container.ListBlobs(useFlatBlobListing: true)
                .Cast<CloudBlockBlob>()
                .Select(b => new TreeViewBlob
                {
                    Name = b.Name, 
                    Blob = b
                });

            var nodes = BuildTree(blobs);

            BuildTreeView(item, nodes);

            return item;
        }

        /// <summary>
        /// Build a tree out of a flat list of blobs.
        /// </summary>
        /// <param name="blobs"></param>
        /// <returns>Top-levle nodes in the tree.</returns>
        private IEnumerable<TreeViewNode> BuildTree(IEnumerable<TreeViewBlob> blobs)
        {
            return blobs
                .GroupBy(b => b.Name.Split('/')[0])
                .Select(g =>
                {
                    var children = g.Where(b => b.Name.Length > g.Key.Length + 1).Select(b => new TreeViewBlob
                    {
                        Name = b.Name.Substring(g.Key.Length + 1),
                        Blob = b.Blob
                    });

                    var blob = g.FirstOrDefault(b => b.Name == g.Key)?.Blob;

                    return new TreeViewNode
                    {
                        Name = g.Key,
                        Blob = blob, 
                        Children = BuildTree(children)
                    };
                });
        }

        /// <summary>
        /// Build the TreeViewItems from the tree.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="nodes"></param>
        private void BuildTreeView(TreeViewItem root, IEnumerable<TreeViewNode> nodes)
        {
            foreach(var node in nodes)
            {
                var item = new TreeViewItem();
                item.Header = node.Name;

                if(node.Blob != null) // Block blob.
                {
                    item.Tag = node.Blob;
                }

                BuildTreeView(item, node.Children);

                root.Items.Add(item);
            }
        }

        /// <summary>
        /// Get the container name and directory path for a directory TreeViewItem.
        /// </summary>
        /// <param name="item"></param>
        /// <returns>The ContainerName and DirectoryName tuple.</returns>
        private (string ContainerName, string DirectoryName) GetContainerAndDirectory(TreeViewItem item)
        {
            var directoryBuilder = new StringBuilder();
            var container = "";
            var current = item;
            while (current != null)
            {
                if (current.Parent is TreeViewItem)
                    directoryBuilder.Insert(0, $"{current.Header.ToString()}/", 1);
                else
                    container = current.Header.ToString();

                current = current.Parent as TreeViewItem;
            }

            var directory = directoryBuilder.ToString();

            return (container, directory);
        }

        /// <summary>
        /// Update blob/directory selection.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void View_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = (sender as TreeView).SelectedItem as TreeViewItem;

            if (item == null)
                return;

            IsDirectorySelected = !(IsBlobSelected = item.Tag != null);
        }

        #endregion
    }

    internal class TreeViewNode
    {
        public string Name { get; set; }
        public CloudBlockBlob Blob { get; set; }
        public IEnumerable<TreeViewNode> Children { get; set; }
    }

    internal class TreeViewBlob
    {
        public string Name { get; set; }
        public CloudBlockBlob Blob { get; set; }
    }
}
