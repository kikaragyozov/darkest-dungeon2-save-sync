using Octokit;
using LibGit2Sharp;
using Repository = LibGit2Sharp.Repository;
using Signature = LibGit2Sharp.Signature;
using Newtonsoft.Json;
using System.Security.Cryptography;

class Program
{
    private const string REPO_NAME = "DarkestDungeon2Sync";
    private static string githubToken;
    private static string saveFolderPath;
    private static GitHubClient githubClient = new(new ProductHeaderValue("ShareDD2ProgressTogether"));
    private static string username;
    private static string repositoryOwner;
    private static bool isOwner;
    private static bool useEncryption;
    private static List<string> collaborators;

    static async Task Main(string[] args)
    {
        string? command = null;
        bool commandHandled = false;
        try
        {
            await Configure();
            
            // Config takes priority before setting up repository.
            if (args.Length > 0)
            {
                command = args[0];
                if (command == "config")
                {
                    await UpdateConfig();
                    commandHandled = true;
                }
            }
            else
            {
                await SetupRepository();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"An error occurred during setup: {e.Message}");
            return;
        }

        if (command is not null && !commandHandled)
        {
            try
            {
                await ProcessCommand(command);
            }
            catch (Exception e)
            {
                Console.WriteLine($"An error occurred: {e.Message}");
            }
        }
        else
        {
            while (true)
            {
                Console.Write($"Enter command (start, end, config,{(isOwner ? " invite," : string.Empty)} or quit): ");
                command = Console.ReadLine();
                try
                {
                    if (command == "quit")
                    {
                        break;
                    }
                    else
                    {
                        await ProcessCommand(command);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"An error occurred: {e.Message}");
                }
            }
        }
    }

    static async Task ProcessCommand(string command)
    {
        if (command == "start")
        {
            await StartRun();
        }
        else if (command == "end")
        {
            await EndRun();
        }
        else if (command == "config")
        {
            await UpdateConfig();
        }
        else if (isOwner && command == "invite")
        {
            await UpdateCollaborators();
        }
        else
        {
            Console.WriteLine("Unknown command.");
        }
    }


    static async Task Configure()
    {
        try
        {
            var tempPath = Path.GetTempPath();
            var configFilePath = Path.Combine(tempPath, "DarkestDungeon2SyncConfig.json");
            var useEncryptionFilePath = Path.Combine(tempPath, "DarkestDungeon2SyncConfig.key");
            try
            {
                if (File.Exists(configFilePath))
                {
                    // Load configuration from file
                    var configJson = File.ReadAllText(configFilePath);
                    if (File.Exists(useEncryptionFilePath))
                    {
                        configJson = Decrypt(configJson, true);
                    }
                    
                    var config = JsonConvert.DeserializeObject<Dictionary<string, string>>(configJson);
                    githubToken = config["githubToken"];
                    saveFolderPath = config["saveFolderPath"];
                    repositoryOwner = config["repositoryOwner"];
                    isOwner = bool.Parse(config["isOwner"]);
                }
                else
                {
                    await UpdateConfig(true);
                }

                ValidateSaveFolderPath();

                githubClient.Credentials = new Octokit.Credentials(githubToken);

                // Fetch and set username using the GitHub token
                username = (await githubClient.User.Current()).Login;
            }
            catch (Exception e)
            {
                File.Delete(configFilePath);
                File.Delete(useEncryptionFilePath);
                Console.WriteLine("The configuration file seems to have been outdated/altered, as such, it has been deleted so you can try again. Application will now exit.");
                Environment.Exit(0);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"An error occurred during the configuration process: {e.Message}");
            Console.WriteLine("Check if your configuration file is properly formatted and accessible.");
        }
    }

    static void ValidateSaveFolderPath()
    {
        while (!Directory.Exists(saveFolderPath))
        {
            Console.WriteLine($"Save folder path '{saveFolderPath}' does not exist. Please enter a valid path.");
            saveFolderPath = Console.ReadLine();
        }
    }

    static async Task SetupRepository()
    {
        try
        {
            // Check if a Git repository is set up at the save folder path
            if (!Directory.Exists(Path.Combine(saveFolderPath, ".git")))
            {
                Console.Write("Setting up the repository...");

                // Check if a repository of the same name exists on GitHub
                Octokit.Repository? repository;

                try
                {
                    repository = await githubClient.Repository.Get(repositoryOwner, REPO_NAME);
                }
                catch (Octokit.NotFoundException e)
                {
                    repository = null;
                }

                if (repository == null)
                {
                    if (!isOwner)
                    {
                        Console.WriteLine("No repository was found. Make sure you've been invited as a collaborator and try again. Application will now exit.");
                        Environment.Exit(0);
                    }

                    // Create a new local repository in the save folder
                    var localRepository = new Repository(Repository.Init(saveFolderPath));

                    // Stage all existing files
                    Commands.Stage(localRepository, "*");

                    // Now create the commit
                    var signature = new Signature(username, $"{username}@noreply.github.com", DateTimeOffset.Now);
                    var initialCommit = localRepository.Commit("Initial commit", signature, signature);

                    LibGit2Sharp.Branch mainBranch;
                    // Create a new 'main' branch pointing at the repository's HEAD
                    if (localRepository.Branches["main"] == null)
                    {
                        // Create a new 'main' branch pointing at the repository's HEAD
                        mainBranch = localRepository.CreateBranch("main", initialCommit);

                        // Set 'main' as the repository's HEAD
                        localRepository.Refs.UpdateTarget("HEAD", "refs/heads/main");

                        // Remove the old 'master' branch
                        var masterBranch = localRepository.Branches["master"];
                        if (masterBranch != null)
                        {
                            localRepository.Branches.Remove(masterBranch);
                        }
                    }

                    // Create a new repository on GitHub
                    var newRepository = new NewRepository(REPO_NAME)
                    {
                        Private = true
                    };

                    repository = await githubClient.Repository.Create(newRepository);

                    // Set origin remote
                    var originRemote = localRepository.Network.Remotes.Add("origin", repository.CloneUrl);

                    // Fetch from the remote repository
                    FetchOptions fetchOptions = new FetchOptions
                    {
                        CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials { Username = githubToken, Password = string.Empty }
                    };
                    Commands.Fetch(localRepository, "origin", new string[] { }, fetchOptions, null);

                    // Set the upstream branch for 'main'
                    mainBranch = localRepository.Branches["main"];
                    localRepository.Branches.Update(mainBranch, b => b.Remote = "origin", b => b.UpstreamBranch = "refs/heads/main");

                    // Create a push options object and set the credentials
                    var pushOptions = new PushOptions
                    {
                        CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials { Username = githubToken, Password = string.Empty }
                    };

                    // Push 'main' branch to 'origin'
                    localRepository.Network.Push(localRepository.Branches["main"], pushOptions);

                    Console.WriteLine($"Created new repository '{repository.Name}' at '{repository.HtmlUrl}'.");
                }
                else
                {
                    // Ask for user permission to replace the contents of the save folder
                    Console.WriteLine((isOwner 
                        ? $"A repository named '{REPO_NAME}' has been found to exist in your github account. "
                        : string.Empty)+
                        "The contents of the save folder will be replaced with those of the repository. Continue? (yes/no)");
                    var response = Console.ReadLine()?.ToLowerInvariant();

                    if (response != "yes")
                    {
                        Console.WriteLine(isOwner 
                            ? "Program cannot proceed until you resolve this conflict manually. Application will now exit."
                            : "As you are sharing the save of someone else, you will need to delete your current save in order to use the other one." +
                            "\nPlease manually backup your current save before proceeding. Application will now exit.");

                        Environment.Exit(0);
                    }

                    Console.Write("Deleting local files...");
                    try
                    {
                        // Delete the contents of the save folder
                        DirectoryInfo di = new(saveFolderPath);

                        foreach (FileInfo file in di.GetFiles())
                        {
                            file.Delete();
                        }
                        foreach (DirectoryInfo dir in di.GetDirectories())
                        {
                            dir.Delete(true);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"\nError deleting local files: {e.Message}");
                        throw;
                    }

                    Console.Write("done.\nCloning repository...");

                    // Clone the repository to the save folder path
                    var cloneOptions = new CloneOptions()
                    {
                        CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials { Username = githubToken, Password = string.Empty }
                    };

                    Repository.Clone(repository.CloneUrl, saveFolderPath, cloneOptions);

                    Console.WriteLine("done.");
                }

                // Perform initial synchronization
                SyncWithRepository();
            }
            else
            {
                Console.WriteLine("Skipping setting up repository as the folder path is already a repository. If you'd like to change the user that owns the repository, you'll need to manually delete the '.git' folder for changes to be applied.");
                // If repository is already set up at save folder path
                SyncWithRepository();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"\nFailed to set up repository: {e.Message}");
            Console.WriteLine("Please verify your GitHub credentials and network connection.");
            Console.WriteLine("If the problem persists, consider reconfiguring your credentials using the 'config' command.");
            throw;
        }
    }

    static async Task StartRun()
    {
        // Sync the repository
        SyncWithRepository();

        string lockFilePath = Path.Combine(saveFolderPath, "lockfile");
        if (File.Exists(lockFilePath))
        {
            Console.WriteLine("Cannot start a new run because a lock file exists.");
            return;
        }

        // Write the username to the lock file
        File.WriteAllText(lockFilePath, username);

        try
        {
            using (var repo = new LibGit2Sharp.Repository(saveFolderPath))
            {
                // Stage and commit
                Commands.Stage(repo, "lockfile");
                var signature = new Signature(username, $"{username}@noreply.github.com", DateTimeOffset.Now);
                var commit = repo.Commit("Start run", signature, signature);

                try
                {
                    Console.WriteLine("Preparing to push changes to the repository. This may take a few moments...");
                    var pushOptions = new PushOptions();
                    pushOptions.CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials { Username = githubToken, Password = string.Empty };
                    repo.Network.Push(repo.Branches["main"], pushOptions);
                }
                catch
                {
                    // If push fails, reset to the commit before the last one.
                    repo.Reset(ResetMode.Hard, commit.Parents.First());
                    throw;
                }
            }
            Console.WriteLine("Run started.");
        }
        catch (LibGit2SharpException e) when (e.Message.Contains("non-fast-forward"))
        {
            // Handle the specific conflict error here
            Console.WriteLine("Cannot start a new run because a run has already been started by another user.");
            // Remove the local lock file
            File.Delete(lockFilePath);
        }
        catch (Exception e)
        {
            Console.WriteLine($"An error occurred during starting the run: {e.Message}");
            // Remove the local lock file in case of other errors
            File.Delete(lockFilePath);
        }
    }

    static async Task EndRun()
    {
        // Sync the repository
        SyncWithRepository();

        string lockFilePath = Path.Combine(saveFolderPath, "lockfile");
        if (!File.Exists(lockFilePath) || !File.ReadAllText(lockFilePath).Equals(username))
        {
            Console.WriteLine("Cannot end the run because you are not the current runner.");
            return;
        }

        try
        {
            using (var repo = new LibGit2Sharp.Repository(saveFolderPath))
            {
                // Add all files in the save folder to the Git index
                Commands.Stage(repo, "*");

                // Remove the lockfile from the index and working directory
                Commands.Remove(repo, "lockfile");

                var signature = new Signature(username, $"{username}@noreply.github.com", DateTimeOffset.Now);
                var commit = repo.Commit("End run", signature, signature);

                try
                {
                    var pushOptions = new PushOptions();
                    pushOptions.CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials { Username = githubToken, Password = string.Empty };
                    repo.Network.Push(repo.Branches["main"], pushOptions);
                }
                catch
                {
                    // If push fails, reset to the commit before the last one.
                    repo.Reset(ResetMode.Hard, commit.Parents.First());
                    throw;
                }

                File.Delete(lockFilePath);
            }

            Console.WriteLine("Run ended.");
        }
        catch (LibGit2Sharp.EmptyCommitException)
        {
            Console.WriteLine("Nothing to commit. The save folder hasn't been changed.");
        }
        catch (Exception e)
        {
            Console.WriteLine($"An unexpected error occurred while ending the run: {e.Message}");
        }
    }


    static async Task<User?> GetAuthenticatedUserInfo(string token)
    {
        GitHubClient client = githubClient;
        client.Credentials = new Octokit.Credentials(token);
        try
        {
            return await client.User.Current();
        }
        catch (Octokit.AuthorizationException)
        {
            return null;
        }
    }

    static async Task UpdateCollaborators()
    {
        Console.Write("Refreshing collaborators...");
        collaborators = await LoadCollaborators();
        Console.WriteLine("done.");

        do
        {
            Console.WriteLine("What do you wish to do?\n\n\t(1) Add collaborator\n\t(2) Remove collaborator\n\t(3) List collaborators\n\t(4) Exit this menu\n\n To make your choice, write the number next to the option you wish to use.");
            string? response = Console.ReadLine();
            if (string.IsNullOrEmpty(response))
            {
                continue;
            }
            else if (response == "1")
            {
                Console.Write("Write the username of the collaborator you wish to invite: ");
                string? collaboratorInviteResponse = Console.ReadLine();
                if (!string.IsNullOrEmpty(collaboratorInviteResponse))
                {
                    await AddCollaborator(collaboratorInviteResponse);
                }
            }
            else if (response == "2")
            {
                Console.Write("Enter name or number corresponding to the collaborator you wish to remove: ");
                string? removeCollaboratorResponse = Console.ReadLine();
                if (removeCollaboratorResponse is not null
                    && removeCollaboratorResponse.All(c => char.IsDigit(c))
                    && int.TryParse(removeCollaboratorResponse, out int collaboratorId)
                    && collaboratorId <= collaborators.Count && collaboratorId != 0)
                {
                    string collaboratorLogin = collaborators[collaboratorId - 1];
                    await RemoveCollaborator(collaboratorLogin);                    
                    collaborators.RemoveAt(collaboratorId - 1);
                    Console.WriteLine($"'{collaboratorLogin}' was removed as a collaborator to the '{repositoryOwner}/{REPO_NAME}' repository.");
                }
                else
                {
                    int collaboratorToRemove = collaborators.IndexOf(removeCollaboratorResponse);
                    if (collaboratorToRemove != -1)
                    {
                        await RemoveCollaborator(removeCollaboratorResponse);
                        collaborators.RemoveAt(collaboratorToRemove);
                        Console.WriteLine($"'{removeCollaboratorResponse}' was removed as a collaborator to the '{repositoryOwner}/{REPO_NAME}' repository.");
                    }
                    else
                    {
                        Console.WriteLine("No such collaborator was found.");
                    }
                };
            }
            else if (response == "3")
            {
                if (collaborators.Count == 0)
                {
                    Console.WriteLine($"There are 0 collaborators for the '{repositoryOwner}/{REPO_NAME}' repository");
                }
                else
                {
                    Console.WriteLine($"Listing {collaborators.Count} collaborators for the '{repositoryOwner}/{REPO_NAME}' repository:\n\n");
                    for (int i = 0; i < collaborators.Count; i++)
                    {
                        Console.WriteLine($"\t({i + 1}) {collaborators[i]}");
                    }
                }
            }
            else if (response == "4")
            {
                return;
            }
        } while (true);
    }

    static async Task UpdateConfig(bool forceUpdate = false)
    {
        var tempPath = Path.GetTempPath();
        var configFilePath = Path.Combine(tempPath, "DarkestDungeon2SyncConfig.json");
        var useEncryptionFilePath = Path.Combine(tempPath, "DarkestDungeon2SyncConfig.key");
        var encryptionFileExists = File.Exists(useEncryptionFilePath);
        if (!forceUpdate)
        {
            Console.WriteLine("Do you wish to modify or reset your configuration? (modify/reset)");
            do
            {
                string? response = Console.ReadLine()?.ToLowerInvariant();
                if (response == "modify")
                {
                    break;
                }
                else if (response == "reset")
                {
                    ResetConfiguration();
                }
                else
                {
                    Console.Write("Please respond with 'modify' to edit configuration, or 'reset' to completely delete it.");
                }
            } while (true);
        }

        bool isInvalidToken;
        do
        {
            bool hasTokenSet = githubToken is not null;
            Console.Write(string.Format("Enter your GitHub personal access token{0}: ", hasTokenSet ? "(leave empty to keep it as-it-is)" : string.Empty));
            var token = Console.ReadLine();
            if (hasTokenSet && string.IsNullOrEmpty(token))
            {
                break;
            }

            isInvalidToken = string.IsNullOrEmpty(token) || token.Length != 40;
            if (isInvalidToken)
            {
                Console.WriteLine("Invalid GitHub token. Please enter a valid 40-character personal access token.");
            }
            else
            {
                // Fetch and set username using the GitHub token
                User? authUser = await GetAuthenticatedUserInfo(token!);
                if (authUser is not null)
                {
                    username = authUser.Login;
                    githubToken = token!;
                    Console.WriteLine($"Authenticated as {username}.");
                }
                else
                {
                    Console.WriteLine("Invalid GitHub token. Please enter a valid 40-character personal access token.");
                    isInvalidToken = true;
                }
            }
        } while (isInvalidToken);

        bool hasPathSet = saveFolderPath is not null;
        string? folderPath;
        do
        {
            Console.Write(string.Format("Enter the path to the Darkest Dungeon 2 save folder{0}: ", hasPathSet ? "(leave empty to keep it as it is)" : string.Empty));
            folderPath = Console.ReadLine();
            if (hasPathSet && string.IsNullOrEmpty(folderPath))
            {
                folderPath = saveFolderPath;
                break;
            }
        } while (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath));

        bool saveFolderChangedOrSet = saveFolderPath != folderPath;
        saveFolderPath = folderPath;

        if (saveFolderChangedOrSet)
        {
            Console.WriteLine("Do you wish to share your own save, or use the save of someone else? (own/shared)");
            do
            {
                string? sharingResponse = Console.ReadLine()?.ToLowerInvariant();
                if (sharingResponse is null)
                {
                    continue;
                }

                if (sharingResponse == "own")
                {
                    isOwner = true;
                    break;
                }
                else if (sharingResponse == "shared")
                {
                    isOwner = false;
                    break;
                }
                else
                {
                    Console.WriteLine("Please respond with 'own' to make your intent clear that you wish to save your own save, or with 'shared' if you'll be sharing someone elses save.");
                }
            } while (true);
        }

        if (!isOwner)
        {
            do
            {
                Console.Write("Enter the username that owns the repository: ");
                repositoryOwner = Console.ReadLine();
            } while (string.IsNullOrEmpty(repositoryOwner) || repositoryOwner == username || await githubClient.User.Get(repositoryOwner) is null);
        }
        else
        {
            repositoryOwner = username;
        }

        do
        {
            Console.WriteLine("Would you like your configuration to be encrypted? Enabling this will require you to enter something like a password every time you start the application. (yes/no)");
            string encryptionResponse = Console.ReadLine()?.ToLowerInvariant();
            if (encryptionResponse == "yes")
            {
                useEncryption = true;
                break;
            }
            else if (encryptionResponse == "no")
            {
                useEncryption = false;
                break;
            }
            else
            {
                Console.WriteLine("Please respond with 'yes' if you'd like to enable encryption, 'no' if you don't.");
            }
        } while (true);

        var config = new Dictionary<string, string>
        {
            { "githubToken", githubToken },
            { "saveFolderPath", saveFolderPath },
            { "isOwner", isOwner.ToString()},
            { "repositoryOwner", repositoryOwner },
        };

        var configJson = JsonConvert.SerializeObject(config);
        if (useEncryption)
        {
            if (!encryptionFileExists)
            {
                string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                Console.WriteLine($"Please save the following secret to a safe place, you will need it to decrypt your configuration: {secret}\nPress any key to continue...");
                Console.ReadLine();
            }

            var encryptedConfigJson = Encrypt(configJson, forceUpdate);
            if (encryptedConfigJson is null)
            {
                Console.WriteLine("Configuration was not updated.");
                return;
            }

            File.WriteAllText(configFilePath, encryptedConfigJson);
            File.WriteAllText(useEncryptionFilePath, null);
        }
        else
        {
            File.WriteAllText(configFilePath, configJson);
            if (encryptionFileExists)
            {
                File.Delete(useEncryptionFilePath);
            }
        }

        Console.WriteLine("Configuration updated.");
        if (!forceUpdate)
        {
            await SetupRepository();
        }
    }

    static void SyncWithRepository()
    {
        try
        {
            Console.Write("Synchronizing with the repository...");

            using (var repo = new LibGit2Sharp.Repository(saveFolderPath))
            {
                // Set up the options for the pull
                var options = new PullOptions
                {
                    FetchOptions = new FetchOptions
                    {
                        CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials
                        {
                            Username = githubToken, // GitHub token as username
                            Password = string.Empty // No password is needed when using a token
                        }
                    }
                };

                // Perform the pull
                var signature = new Signature(username, $"{username}@noreply.github.com", DateTimeOffset.Now);
                Commands.Pull(repo, signature, options);
            }

            Console.WriteLine("done.");
        }
        catch (Exception e)
        {
            Console.WriteLine($"\nAn error occurred during synchronization: {e.Message}");
            Console.WriteLine("Please verify your GitHub credentials, network connection, and repository accessibility.");
            throw;
        }
    }

    public static async Task AddCollaborator(string collaboratorUsername)
    {
        try
        {
            // Add the collaborator
            if (collaboratorUsername == username)
            {
                Console.WriteLine($"You can't add yourself as a collaborator to the '{repositoryOwner}/{REPO_NAME}' repository.");
            }
            else
            {
                await githubClient.Repository.Collaborator.Add(username, REPO_NAME, collaboratorUsername);
                Console.WriteLine($"'{collaboratorUsername}' was added as a collaborator to the '{repositoryOwner}/{REPO_NAME}' repository.");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"An error occurred while adding collaborator: {e.Message}");
        }
    }

    public static async Task RemoveCollaborator(string username)
    {
        try
        {
            await githubClient.Repository.Collaborator.Delete(repositoryOwner, REPO_NAME, username);
        }
        catch (Exception e)
        {
            Console.WriteLine($"An error occurred when removing a collaborator: {e.Message}");
        }
    }

    public static async Task<List<string>> LoadCollaborators()
    {
        try
        {
            return (await githubClient.Repository.Collaborator.GetAll(repositoryOwner, REPO_NAME)).Select(c => c.Login).Where(l => l != username).ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine($"\nAn error occurred: {e.Message}");
            throw;
        }
    }


    static string? Encrypt(string text, bool requestShutdownOnFailure = false)
    {
        string? secretKey;
        do
        {
            beginning:
            Console.Write("You have encryption enabled. Please enter your secret key: ");
            secretKey = Console.ReadLine();
            if (string.IsNullOrEmpty(secretKey))
            {
                continue;
            }

            try
            {
                return Convert.ToBase64String(AesService.Encrypt(text, secretKey));
            }
            catch
            {
                do
                {
                    Console.Write($"Incorrect key. Would you like to reset your configuration? (yes/no/retry): ");
                    string? userResponse = Console.ReadLine()?.ToLowerInvariant();
                    if (userResponse == "yes")
                    {
                        ResetConfiguration();
                    }
                    else if (userResponse == "retry")
                    {
                        goto beginning;
                    }
                    else if (userResponse == "no")
                    {
                        if (requestShutdownOnFailure)
                        {
                            Console.WriteLine("Application will now exit.");
                            Environment.Exit(0);
                        }

                        return null;
                    }
                } while (true);
            }
        } while (true);
    }

    static string? Decrypt(string encryptedText, bool requestShutdownOnFailure = false)
    {
        byte[] payload = Convert.FromBase64String(encryptedText);
        string? secretKey;
        do
        {
            beginning:
            Console.Write("You have encryption enabled. Please enter your secret key: ");
            secretKey = Console.ReadLine();
            if (string.IsNullOrEmpty(secretKey))
            {
                continue;
            }

            try
            {
                return AesService.Decrypt(payload, secretKey);
            }
            catch
            {
                do
                {
                    Console.Write($"Incorrect key. Would you like to reset your configuration? (yes/no/retry): ");
                    string? userResponse = Console.ReadLine()?.ToLowerInvariant();
                    if (userResponse == "yes")
                    {
                        ResetConfiguration();
                    }
                    else if (userResponse == "retry")
                    {
                        goto beginning;
                    }
                    else if (userResponse == "no")
                    {
                        if (requestShutdownOnFailure)
                        {
                            Console.WriteLine("Application will now exit.");
                            Environment.Exit(0);
                        }

                        return null;
                    }
                } while (true);
            }
        } while (true);
    }

    static void ResetConfiguration()
    {
        var tempPath = Path.GetTempPath();
        var configFilePath = Path.Combine(tempPath, "DarkestDungeon2SyncConfig.json");
        var useEncryptionFilePath = Path.Combine(tempPath, "DarkestDungeon2SyncConfig.key");

        File.Delete(configFilePath);
        File.Delete(useEncryptionFilePath);
        Console.WriteLine("Configuration was reset. Application will now exit.");
        Environment.Exit(0);
    }
}
