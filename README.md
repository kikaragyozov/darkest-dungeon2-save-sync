# Darkest Dungeon 2 Save Synchronizer

## Table of Contents
- [Introduction](#introduction)
- [How it Works](#how-it-works)
- [Usage](#usage)
  - [Setting up your GitHub token](#setting-up-your-github-token)
  - [Running the application](#running-the-application)
- [Troubleshooting](#troubleshooting)
- [Contributing](#contributing)
- [License](#license)

## Introduction

Darkest Dungeon 2 Save Synchronizer is a command-line tool designed to facilitate the sharing and synchronization of game save files for Darkest Dungeon 2 via GitHub. By using this tool, a player ("owner") can share their game progress with others ("guests"), who can in turn contribute to the same save file and share their progress in return.

## How it Works

The application leverages the GitHub API and Git's native capabilities to manage synchronization. Upon launching the application, you'll be prompted to enter your GitHub credentials and the directory path for your Darkest Dungeon 2 save files. The application will then initialize a Git repository in your save directory and establish a corresponding repository on GitHub, if one doesn't already exist.

The application provides a series of commands to facilitate interactions with the shared save. Here are the commands currently available:

- `start`: Commences a new game session. This command creates a "lock" file in the repository to indicate that the save file is currently in use. Other users will not be able to start their own sessions until you finish yours. The 'start' command also synchronizes the local save files with the latest version on GitHub.
- `end`: Ends the current game session. This command deletes the "lock" file, commits all changes made to the save files during the session to the Git repository, and then pushes the commit to GitHub.
- `invite`: Available only to users who have opted to share their own save file. This command allows the user to add, remove, or list collaborators for the repository they control. This option is not available to users who wish to access another person's save file.
- `config`: Enables modification or resetting of your application configuration, such as your GitHub credentials and save folder location.

The process behind the application's functionality is as follows: All modifications to the save files are tracked by Git. When you commence a game session with the `start` command, a lock file is created to indicate that the save file is in use. Upon ending your session with the `end` command, any alterations you've made to the save files are committed to the Git repository and the lock file is deleted. These commits are then pushed to GitHub, enabling others to pull the changes and continue from where you left off.

Guest users can join a shared save only after being invited by the owner to become a collaborator. Once they have been invited and have configured the application with their GitHub credentials and save folder location, they can start contributing to the game save.

## Usage

### Setting up your GitHub token

1. Visit [https://github.com/settings/tokens](https://github.com/settings/tokens) and click on the "Generate new token" button.
2. In the "Note" field, assign a name for the token.
3. Check the "repo" checkbox to grant full control over repositories.
4. Click on the "Generate token" button at the bottom of the page.
5. Copy the generated token and paste it into the application when requested.

### Running the application

1. Launch the application from the command line with `dotnet DarkestDungeon2Sync.dll`.
2. Follow the prompts to input your GitHub credentials, specify your save directory, and determine whether you wish to share your own save or join someone else's.
3. Use the available commands (`start`, `end`, `invite`, `config`) to interact with the shared save.

## Troubleshooting

1. I can't connect to GitHub.
    * Make sure your GitHub token is correct and has the necessary permissions (full control over repositories). Try generating a new token if necessary.
   
2. I can't synchronize my local save with the one on GitHub.
   * Check if your local save directory is correct and contains valid save files.

3. I can't invite new collaborators.
   * The 'invite' command is available only to owners. If you are an owner, ensure the username of the guest is correct and that they are not already a collaborator.
   
4. I changed the user who owns the repository but it's not taking effect!
   * Once a repository has been detected in your save file path, the application will not touch it as it assumes you've manually configured it to the correct one. If you'd like the application to take over, simply delete the hidden '.git' folder and watch the show unravel.

5. I can't change myself to be an owner by using `config`.
   * This is a known issue. If you have the time, feel free to submit a pull request fixing this. The workaround is to do a full config reset, instead of using the 'modify' feature.

6. Nothing works! It's all a mess! What have you done!?
   * The application was written rather hastily. If you spot any bugs, or issues, feel free to open an issue on Github. Contributions are also welcome. It cannot delete your local files, unless you explicitly agree to it.

For more complex issues, please consider opening an issue on GitHub.

## Contributing

Contributions to this project are warmly welcomed! Feel free to submit a pull request or open an issue on GitHub. Your contributions will be mentioned.

## Contributors

A big thanks to the following contributors and people:

- [ToonStar](https://github.com/ToonStar) - Inspiration for initial idea

## License

This project is licensed under the [MIT License](LICENSE).
