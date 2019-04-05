# Contributing

## Pulling the project

To pull the project, issue these commands in the git shell:

```bash
cd "/c/Program Files (x86)/Steam/steamapps/common/RimWorld/Mods"
git clone git@github.com:Parexy/Multiplayer.git
```

If your RimWorld installation is in a non-standard location, cd to that `Mods` directory first before cloning. 

## Building the project

### Publicizing the RimWorld assembly

Before building the project, you need to make a special version of the RimWorld assembly that has all its internal members/classes public. You can either write your own program to do this or use the tool [here](https://github.com/jacobEAdamson/publicize/releases)

Once you've downloaded the tool, simply run this command from git bash:

```bash
./Publicize "/c/Program Files (x86)/Steam/steamapps/common/RimWorld/RimWorldWin64_Data/Managed/Assembly-CSharp.dll"
```

This will generate the assembly in the correct location for the Multiplayer mod to use.

## Building the project

Once you've got your assemblies in the correct spots, you can open up the `Source/Multiplayer.sln` file in your IDE of choice and press `ctl-shift-b` to build the assemblies. You should now have a debug version of the Multiplayer mod.

## Completing an issue

You're now ready to start an issue. Assign yourself to the issue you want to complete and make a branch for it. Please include the number of the issue in the branch name for easy traceability of feature branches (Example: `issue-9-feature`). Complete the feature and test thoroughly before making your PR.

Please prefix all commit messages in your PR with `#{issue_number}` to help with auto-linking to issue (Example `#9: create initial contributors document`).

## Making a PR

Make a PR to commit your code to the master branch. At this point, at least one reviewer needs to sign off before the code can be merged. They may also have comments or requests for changes. Please work with the reviewer until they are satisfied. Once the approval is given, merge your branch via the `Squash and Merge` merge method.