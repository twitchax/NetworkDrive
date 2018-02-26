# Azure Blob Storage as a Network Drive

Many applications make use of a network drive to backup and store files. If you were, for example, constantly coding at University for fun, you might find yourself finding software solutions for fun. In my case, this took the form of a network share for my roommates wrapped in a handy little app.

Unfortunately, that very app has long since been erased from whichever hard drive it was initially birthed. Fortunately, I think we can reinvent this magical piece of software (albeit to a scoped degree) with [Azure Blob Storage](https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blobs-introduction). In the past, network drives did the trick, but Azure Storage offers users better flexibility and global availability, [all at a very low cost](https://azure.microsoft.com/en-us/pricing/details/storage) (or no cost if you are using [free Azure credits](https://azure.microsoft.com/en-us/free)).

I took my partial memory of the general skeleton of the former masterpiece and rewrote it using Blobs as the backing file store. We are going to build this app as it was in its glory days, which means we need a few things.

## Prerequisites

I built this app in [WPF](https://docs.microsoft.com/en-us/dotnet/framework/wpf/getting-started/introduction-to-wpf-in-vs) using [Visual Studio 2017](https://www.visualstudio.com/).  In addition, I used the [Azure Storage for .NET](https://www.nuget.org/packages/WindowsAzure.Storage/) library.  Finally, you will notice that I left off a few components that would be required in a production app: for purposes of reducing code, the authentication method requires a [Storage account](https://docs.microsoft.com/en-us/azure/storage/common/storage-create-storage-account) key, and I have kept the UI strictly utilitarian.

While I made the choice to build this app in WPF, the following Azure Storage solution works with any .NET Application type including Windows Forms, ASP.NET, Console, etc.

## Representing the Account as a TreeView

At its core, the app needs to display the current state of the blob account in a human-consumable fashion.  In a stroke of ingenuity, I decided to represent the containers, directories, and blobs as a [TreeView](https://msdn.microsoft.com/en-us/library/system.windows.controls.treeview.aspx).  There are some existing solutions for computing trees from a flat list of blobs, but I opted to build it out myself since Azure Storage makes the rest of the app a breeze.

As expected, we must build a tree for each container.  First, we must obtain a flat list of the blobs in the container.

```csharp
var blobs = container.ListBlobs(useFlatBlobListing: true)
    .Cast<CloudBlockBlob>()
    .Select(b => new TreeViewBlob
    {
        Name = b.Name, 
        Blob = b
    });
```

The `TreeViewBlob` is just a convenient representation for the following tree building algorithm.

```csharp
IEnumerable<TreeViewNode> BuildTree(IEnumerable<TreeViewBlob> blobs)
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
```

The above two listings takes a flat list of blobs.

```
my/share/file1.jpg
my/share/file2.jpg
my/share/private/file.jpg
```

Subsequent to `BuildTree`, we essentially have the structure of our `TreeView`.

```
my
  share
    file1.jpg
    file2.jpg
    private
        file.jpg
```

Now that we have built the `TreeView`, we need to start implementing our Storage commands.

## Downloading Blobs

The first of our three action buttons downloads a blob.  As I mentioned earlier, Blob storage makes this task excessively simple.

```csharp
async void DownloadButton_Click(object sender, RoutedEventArgs e)
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
```

This method takes the currently selected `BlockBlob`, displays a prompt to the user, and downloads the blob to the selected file.  The real meat of this method is performed during `blob.DownloadToFileAsync`; the rest of the method is just gathering the proper information.  

Both `async` and `await` have drastically simplified UI thread updates in presentation frameworks, so setting the status text and using `await` gives us the desired UI results.

Downloading blobs is only half the excitement: we need the ability to upload blobs, as well.

## Uploading Blobs

Upon selecting a directory, the "upload" button enables the user to select a file and upload it into the Storage directory.

```csharp
async void UploadButton_Click(object sender, RoutedEventArgs e)
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
```

Similar to the download method, the upload method prompts the user for a file, and uploads the file into a blob of the same name into the, currently selected, directory.  Again, most of the method is simply gathering the proper data, while the Storage library simplifies the operation into one call (`blobReference.UploadFromFileAsync`).  However, this is the first and only time we will come across a "reference".  Prior to uploading the file into a blob, a local `CloudBlockBlob` reference is ascertained via `container.GetBlockBlobReference`.

After muddying up my test storage account with a bunch of uploaded files, I decided it was time to implement the ability to delete blobs.

## Deleting Blobs

Selecting a block blob in the tree will also allow the user to delete the selected blob.

```csharp
async void DeleteButton_Click(object sender, RoutedEventArgs e)
{
    var item = View.SelectedItem as TreeViewItem;
    var blob = item.Tag as CloudBlockBlob;
    var name = item.Header as string;

    StatusText = "Deleting...";
    await blob.DeleteAsync();
    await UpdateView();
    StatusText = "Success!";
}
```

As was the case for downloading blobs, the `CloudBlockBlob` is easily attained from the `TreeViewItem`, having been attached previously.  Again, performing the actual operation only requires only one call to `blob.DeleteAsync`.

## The User Interface

As I mentioned previously, the Azure Storage solution we built is applicable to any type of .NET Application.  I decided to use WPF, but the choice for this specific endeavour was made out of a personal love for XAML.

As a former developer on the [XAML](https://docs.microsoft.com/en-us/dotnet/framework/wpf/advanced/xaml-overview-wpf) Developer Platform Team in Windows, I am always excited to stretch my XAML skills after a long time off.  Despite getting back into it, I did not really have much to do.  So little, in fact, I decided to completely ignore using `Style`s.  Our XAML looks something like this.

```xml
<Grid>
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="0,10,10,10" Width="400" Height="30">
        <Button Name="DownloadButton" Content="Download" IsEnabled="{Binding IsBlobSelected}" Click="DownloadButton_Click" Margin="10,0,0,0" Width="100" Height="30"></Button>
        <Button Name="UploadButton" Content="Upload" IsEnabled="{Binding IsDirectorySelected}" Click="UploadButton_Click" Margin="10,0,0,0" Width="100" Height="30"></Button>
        <Button Name="DeleteButton" Content="Delete" IsEnabled="{Binding IsBlobSelected}" Click="DeleteButton_Click" Margin="10,0,0,0" Width="100" Height="30"></Button>
    </StackPanel>
    <TreeView Name="View" Margin="10,50,10,50" SelectedItemChanged="View_SelectedItemChanged" />
    <TextBlock Name="Status" Text="{Binding StatusText}" HorizontalAlignment="Left" VerticalAlignment="Bottom" Margin="10" Width="400" Height="30"></TextBlock>
</Grid>
```

## Putting It All Together

So, there we have it.  We have built an app that acts as a network drive using Azure Blob Storage.  Implementing more operations and polishing th UI is merely an exercise in elbow grease.  If you would like to try out this app, or use the code we went through as a base, take a look at the [source](https://github.com/twitchax/networkdrive).

I hope the work we did here excites you to take existing applications or ideas and port them to equivalent functionality in Azure!