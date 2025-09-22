### C# Advanced Terminal (CAT) Contribution Guide

Thank you for your interest in contributing to the C# Advanced Terminal (CAT) project! Your contributions, big or small, are what make this project better for everyone. By contributing, you agree to abide by our [License and Terms of Use and Contribution Guide](https://github.com/lunaNoir25/CAT/blob/CAT/LICENSE.txt).

---

### 1. How Can I Contribute?

There are many ways to contribute to CAT. You don't have to be a coding expert to make a difference.

* **Report Bugs:** If you find a bug, please create a new issue on our GitHub repository. Provide a clear description of the issue and steps to reproduce it. Screenshots or terminal output are very helpful.
* **Suggest Features:** Have an idea for a new command, a quality-of-life improvement, or a major new feature? Open a discussion or a feature request issue to share your thoughts.
* **Write Code:** We welcome all code contributions, whether it's a bug fix, a new bundle command, or a major refactor.
* * Please note that base installation bundles are in a seperate repository, contribute to it here: [CAT-Data](https://github.com/lunaNoir25/CAT-data)
* **Improve Documentation:** Good documentation is crucial for an open-source project. You can help by clarifying existing docs, fixing typos, or writing new guides.
* **Provide Feedback:** Join our discussions and give your feedback on new features, design decisions, and future plans.

---

### 2. Getting Started with Code

If you're ready to write some code, here's the recommended workflow:

1.  **Fork the Repository:** Start by forking the main CAT repository to your personal GitHub account.
2.  **Clone Your Fork:** Clone your forked repository to your local machine using `git clone`.
3.  **Create a New Branch:** Before you make any changes, create a new branch for your work. Use a descriptive name like `bugfix/fix-pipe-issue` or `feature/add-ls-command`.
4.  **Make Your Changes:** Write your code, following the project's coding style and conventions.
5.  **Test Your Changes:** Run the application and test your changes thoroughly to ensure they work as expected and try your best to not introduce new bugs.
6.  **Commit Your Changes:** Commit your changes with a clear and concise commit message.
7.  **Push to Your Fork:** Push your new branch to your forked repository on GitHub.
8.  **Open a Pull Request:** Go to the original CAT repository and open a pull request. This will propose your changes to be merged into the main project.

---

### 3. Writing New Commands (Bundles)

One of the easiest ways to add functionality to CAT is by creating a new bundle.

* **Location:** Place your C# script file (`.cs`) in the `bundles` directory within your data folder. This is where CAT looks for commands to compile. On Windows, it is `C:\Users\<user>\AppData\Roaming\CAT\bundles\`, on Linux, it is `/home/<user>/.config/CAT/bundles/`
* **Structure:** Your bundle should contain a class and a public method that accepts arugments (`string[] args`) and CAT's cancellation token (`CancellationToken token`). For example:

```csharp
public class BundleName // The name will be the prefix.
{
    public void Command(string[] args, CancellationToken token) // The name will be the command name.
    {
        Console.WriteLine("Hello, CAT!");
    }
}
```
* **Running:** You can easily run new bundles if they were successfully compiled by just simply typing the prefix/Bundle Name and subfix/Command, seperated by a period: `bundlename.command`. It is case-insensitive.
