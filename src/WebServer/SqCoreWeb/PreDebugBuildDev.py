import os
import platform
import sys

print("SqBuild: Python ver: " + platform.python_version() + " (" + platform.architecture()[0] + "), CWD:'" + os. getcwd() + "'")
if (os.getcwd().endswith("SqCore")) : # VsCode's context menu 'Run Python file in Terminal' runs it from the workspace folder. VsCode F5 runs it from the project folder. We change it to the project folder
    os.chdir(os.getcwd() + "/src/WebServer/SqCoreWeb")

# 1. Basic checks: Ensure Node.js is installed. If node_modules folder is empty, it should restore Npm packages.
nodeTouchFile = os.getcwd() + "/node_modules/.install-stamp"
if os.path.isfile(nodeTouchFile):
    print ("SqBuild: /node_modules/ exist")
else:
    nodeRetCode = os.system("node --version")   # don't want to run 'node --version' all the times. If stamp file exists, assume node.exe is installed
    if (nodeRetCode != 0) :
        sys.exit("SqBuild: Node.js is required to build and run this project. To continue, please install Node.js from https://nodejs.org/")
    angularRetCode = os.system("ng --version")
    if (angularRetCode != 0) :
        sys.exit("SqBuild: NodeJs's AngularCLI is required to build and run this project. To continue, please install 'npm install -g @angular/cli@9.0.0-rc.10' on (2020-01-29) ")
    os.system("npm install")
    Path(nodeTouchFile).touch()

# 2. DotNet (C#) build DEBUG
os.system("dotnet build --configuration Debug SqCoreWeb.csproj /property:GenerateFullPaths=true")

