using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FalloutTerminal
{
    public class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TerminalForm());
        }
    }

    public class TerminalForm : Form
    {
        private RichTextBox consoleOutput;
        private System.Windows.Forms.Timer typingTimer;
        private Queue<string> textQueue = new();
        private Queue<Color> colorQueue = new();
        private StringBuilder currentTypingText = new();

        private readonly Color terminalGreen = Color.FromArgb(33, 253, 17);
        private readonly Color terminalBackground = Color.FromArgb(12, 12, 12);
        private readonly Font terminalFont = new("Consolas", 14, FontStyle.Bold);
        private readonly Color terminalHighlight = Color.FromArgb(255, 255, 100);
        private readonly Color terminalDim = Color.FromArgb(20, 120, 20);
        private readonly Color terminalError = Color.FromArgb(255, 100, 100);

        private System.Media.SoundPlayer terminalClick;
        private System.Media.SoundPlayer terminalBeep;
        private System.Media.SoundPlayer terminalSuccess;
        private System.Media.SoundPlayer terminalFail;

        private bool bootSequenceComplete = false;
        private bool gameInProgress = false;
        private int attempts = 4;
        private Random random = new();

        private TerminalEngine engine;
        private List<string> wordList;
        private string password;

        private Dictionary<string, string> memoryDump;
        private List<(string word, int start, int end, bool isPassword)> wordRanges = new();
        private List<(string code, int start, int end)> bracketCodes = new();
        private List<string> hexAddresses;

        private int wordLength = 7; // Default to NORMAL difficulty
        private string[] difficulties = { "NOVICE", "ADVANCED", "EXPERT", "MASTER" };
        private int currentDifficulty = 1; // Default to ADVANCED
        private bool selectingDifficulty = true;
        private string lastGuess = string.Empty;
        private int lastLikeness = 0;
        private bool showingHelp = false;
        private List<string> guessHistory = new();

        public TerminalForm()
        {
            InitializeComponents();
            LoadSoundEffects();
            ShowDifficultySelection();
        }

        private void InitializeComponents()
        {
            this.Text = "FALLOUT TERMINAL (v0.0.9c) by aZebra_";
            this.Size = new Size(1000, 700);
            this.BackColor = terminalBackground;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = CreateTerminalIcon();
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            consoleOutput = new RichTextBox
            {
                ReadOnly = true,
                BackColor = terminalBackground,
                ForeColor = terminalGreen,
                Font = terminalFont,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Multiline = true,
                TabStop = false,
                Padding = new Padding(10),
                HideSelection = false,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.None
            };
            consoleOutput.MouseClick += ConsoleOutput_MouseClick;
            consoleOutput.MouseMove += ConsoleOutput_MouseMove;
            consoleOutput.KeyDown += ConsoleOutput_KeyDown;
            consoleOutput.MouseLeave += ConsoleOutput_MouseLeave;
            this.Controls.Add(consoleOutput);
            this.KeyPreview = true;
            this.KeyDown += TerminalForm_KeyDown;

            typingTimer = new System.Windows.Forms.Timer { Interval = 15 };
            typingTimer.Tick += TypingTimer_Tick;

            engine = new TerminalEngine();
            engine.LoadWordList();
        }

        private Icon CreateTerminalIcon()
        {
            Bitmap bitmap = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(terminalBackground);
                g.FillRectangle(new SolidBrush(terminalGreen), 4, 4, 24, 24);
                g.FillRectangle(new SolidBrush(terminalBackground), 8, 8, 16, 16);
            }
            IntPtr hIcon = bitmap.GetHicon();
            return Icon.FromHandle(hIcon);
        }

        private void LoadSoundEffects()
        {
            try
            {
                string appPath = Path.GetDirectoryName(Application.ExecutablePath);
                string soundsPath = Path.Combine(appPath, "Sounds");

                if (!Directory.Exists(soundsPath))
                {
                    Directory.CreateDirectory(soundsPath);
                }

                terminalClick = new System.Media.SoundPlayer();
                terminalBeep = new System.Media.SoundPlayer();
                terminalSuccess = new System.Media.SoundPlayer();
                terminalFail = new System.Media.SoundPlayer();

                // Default to system sounds if files aren't available
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch
            {
                // Silently fail if sound system isn't working
            }
        }

        private void ShowDifficultySelection()
        {
            selectingDifficulty = true;
            consoleOutput.Clear();
            AppendText("> ROBCO INDUSTRIES (TM) TERMLINK PROTOCOL\n", terminalGreen);
            AppendText("> ENTER PASSWORD NOW\n\n", terminalGreen);
            AppendText("SELECT DIFFICULTY:\n", terminalGreen);

            for (int i = 0; i < difficulties.Length; i++)
            {
                string line = (i == currentDifficulty ? "> " : "  ") + difficulties[i] + " ";
                int wordLen = 4 + i + 2; // Word length for each difficulty
                line += $"({wordLen} CHARS)\n";
                AppendText(line, i == currentDifficulty ? terminalHighlight : terminalGreen);
            }

            AppendText("\nUse UP/DOWN arrows and ENTER to select difficulty.", terminalDim);

            // Remove previous event handler to prevent multiple subscriptions
            this.KeyDown -= DifficultySelect_KeyDown;
            this.KeyDown += DifficultySelect_KeyDown;
        }

        private void DifficultySelect_KeyDown(object sender, KeyEventArgs e)
        {
            if (!selectingDifficulty) return;

            e.Handled = true; // Prevent key event bubbling

            if (e.KeyCode == Keys.Up)
            {
                PlaySound(terminalClick);
                currentDifficulty = (currentDifficulty - 1 + difficulties.Length) % difficulties.Length;
                ShowDifficultySelection();
            }
            else if (e.KeyCode == Keys.Down)
            {
                PlaySound(terminalClick);
                currentDifficulty = (currentDifficulty + 1) % difficulties.Length;
                ShowDifficultySelection();
            }
            else if (e.KeyCode == Keys.Enter)
            {
                PlaySound(terminalBeep);
                selectingDifficulty = false;
                // Unsubscribe from the event to prevent multiple handlers
                this.KeyDown -= DifficultySelect_KeyDown;
                wordLength = 4 + currentDifficulty + 2; // 6, 7, 8, 9 for each difficulty
                StartBootSequence();
            }
        }

        private void TerminalForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1 && bootSequenceComplete && !selectingDifficulty)
            {
                if (!showingHelp)
                {
                    ShowHelp();
                }
                else
                {
                    showingHelp = false;
                    DrawMemoryDump();
                }
            }
        }

        private void ShowHelp()
        {
            showingHelp = true;
            consoleOutput.Clear();
            AppendText("> ROBCO INDUSTRIES (TM) TERMLINK PROTOCOL\n", terminalGreen);
            AppendText("> HELP SYSTEM\n\n", terminalGreen);

            AppendText("TERMINAL HACKING INSTRUCTIONS:\n\n", terminalHighlight);

            AppendText("1. Find a password hidden among the characters displayed in the memory dump.\n", terminalGreen);
            AppendText("2. Click on a word to guess. All words are " + wordLength + " characters long.\n", terminalGreen);
            AppendText("3. If your guess is incorrect, you'll see a 'Likeness' score showing how many\n   characters match the position in the correct password.\n", terminalGreen);
            AppendText("4. Use the likeness information to narrow down the correct password.\n", terminalGreen);
            AppendText("5. Special bracket pairs like [], (), {}, or <> can be clicked to either\n   remove a dud password or reset your attempt counter.\n\n", terminalGreen);

            AppendText("CONTROLS:\n", terminalHighlight);
            AppendText("- Mouse: Click on words to guess or bracket pairs for bonuses\n", terminalGreen);
            AppendText("- F1: Toggle this help screen\n", terminalGreen);
            AppendText("- ESC: Exit game\n\n", terminalGreen);

            if (guessHistory.Count > 0)
            {
                AppendText("GUESS HISTORY:\n", terminalHighlight);
                foreach (var guess in guessHistory)
                {
                    AppendText($"{guess}\n", terminalGreen);
                }
                AppendText("\n", terminalGreen);
            }

            AppendText("\nPress F1 to return to the game.", terminalDim);
        }

        private void AddTextToTypingQueue(string text, Color color)
        {
            textQueue.Enqueue(text);
            colorQueue.Enqueue(color);

            if (!typingTimer.Enabled)
                typingTimer.Start();
        }

        private void TypingTimer_Tick(object sender, EventArgs e)
        {
            if (textQueue.Count == 0 && currentTypingText.Length == 0)
            {
                typingTimer.Stop();
                return;
            }

            if (currentTypingText.Length == 0 && textQueue.Count > 0)
            {
                string nextText = textQueue.Dequeue();
                Color nextColor = colorQueue.Dequeue();
                currentTypingText.Append(nextText);
                consoleOutput.SelectionStart = consoleOutput.TextLength;
                consoleOutput.SelectionLength = 0;
                consoleOutput.SelectionColor = nextColor;
            }

            if (currentTypingText.Length > 0)
            {
                char nextChar = currentTypingText[0];
                consoleOutput.AppendText(nextChar.ToString());
                currentTypingText.Remove(0, 1);
                if (nextChar != ' ' && nextChar != '\n' && nextChar != '\r' && random.Next(5) == 0)
                    PlaySound(terminalClick);
            }

            // Auto-scroll to ensure text is visible
            consoleOutput.SelectionStart = consoleOutput.Text.Length;
            consoleOutput.ScrollToCaret();
        }

        private async void StartBootSequence()
        {
            consoleOutput.Clear();
            AddTextToTypingQueue("ROBCO INDUSTRIES (TM) TERMLINK PROTOCOL\n", terminalGreen);
            AddTextToTypingQueue("INITIALIZING BOOT SEQUENCE...\n\n", terminalGreen);
            await WaitForTyping();
            await Task.Delay(500);

            AddTextToTypingQueue("SYSTEM INITIALIZING...\n", terminalGreen);
            await WaitForTyping();
            await Task.Delay(300);

            AddTextToTypingQueue("CPU CHECK... ", terminalGreen);
            await WaitForTyping();
            await Task.Delay(200);
            AddTextToTypingQueue("OK\n", terminalHighlight);

            AddTextToTypingQueue("MEMORY ARRAY CHECK... ", terminalGreen);
            await WaitForTyping();
            await Task.Delay(200);
            AddTextToTypingQueue("OK\n", terminalHighlight);

            AddTextToTypingQueue("INITIALIZING SYSTEM SECURITY... ", terminalGreen);
            await WaitForTyping();
            await Task.Delay(500);
            AddTextToTypingQueue("OK\n", terminalHighlight);

            AddTextToTypingQueue("LOADING ROBCO OS v2.76.0.1...\n", terminalGreen);
            await WaitForTyping();
            await Task.Delay(1000);

            AddTextToTypingQueue("PASSWORD REQUIRED\n\n", terminalError);
            await WaitForTyping();
            await Task.Delay(500);

            AddTextToTypingQueue("BOOT SEQUENCE COMPLETE\n\n", terminalGreen);
            await WaitForTyping();

            bootSequenceComplete = true;
            gameInProgress = true;
            PlaySound(terminalBeep);
            StartGame();
        }

        private async Task WaitForTyping()
        {
            while (textQueue.Count > 0 || currentTypingText.Length > 0)
                await Task.Delay(50);
        }

        private void StartGame()
        {
            engine.InitializeGame(wordLength);
            wordList = engine.GetWordList();
            password = engine.GetPassword();
            hexAddresses = GenerateHexAddresses();
            memoryDump = engine.GetFormattedMemoryDump(hexAddresses, wordList);
            DrawMemoryDump();
        }

        private void DrawMemoryDump()
        {
            consoleOutput.Clear();
            wordRanges.Clear();
            bracketCodes.Clear();

            AppendText("> ROBCO INDUSTRIES (TM) TERMLINK PROTOCOL\n", terminalGreen);
            AppendText("> ENTER PASSWORD NOW\n\n", terminalGreen);

            if (lastGuess != string.Empty)
            {
                AppendText($"> Attempted Password: {lastGuess}\n", terminalGreen);
                if (lastLikeness >= 0)
                {
                    AppendText($"> Entry denied. Likeness: {lastLikeness}/{wordLength}\n", terminalError);
                }
                AppendText("\n", terminalGreen);
            }

            int columns = 2;
            int rows = (hexAddresses.Count + columns - 1) / columns;

            for (int row = 0; row < rows; row++)
            {
                string line = "";
                for (int col = 0; col < columns; col++)
                {
                    int index = row + rows * col;
                    if (index < hexAddresses.Count)
                    {
                        string hex = hexAddresses[index];
                        string data = memoryDump[hex];
                        line += $"{hex} {data}  ";
                    }
                }

                // Process entire line at once after it's fully constructed
                consoleOutput.AppendText(line + "\n");

                // Now process the line for words and brackets
                ProcessTextSegment(line);
            }

            DrawAttempts();
            AppendText("\nPress F1 for help.", terminalDim);
        }

        private void ProcessTextSegment(string line)
        {
            // Track bracket pairs
            List<char> openingBrackets = new() { '[', '{', '(', '<' };
            List<char> closingBrackets = new() { ']', '}', ')', '>' };
            Stack<(char bracket, int position)> bracketStack = new();

            // Find all words in the line
            foreach (string word in wordList)
            {
                int startPos = 0;
                while ((startPos = line.IndexOf(word, startPos)) != -1)
                {
                    // Calculate length of entire console output before adding this word
                    int basePos = consoleOutput.Text.Length - line.Length - 1 + startPos;
                    wordRanges.Add((word, basePos, basePos + word.Length, word == password));
                    startPos += 1;
                }
            }

            // Find bracket pairs in the line
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                int openingIndex = openingBrackets.IndexOf(c);

                if (openingIndex != -1)
                {
                    bracketStack.Push((c, consoleOutput.Text.Length - line.Length - 1 + i));
                }
                else
                {
                    int closingIndex = closingBrackets.IndexOf(c);
                    if (closingIndex != -1 && bracketStack.Count > 0)
                    {
                        var opening = bracketStack.Pop();
                        int matchIndex = openingBrackets.IndexOf(opening.bracket);

                        if (matchIndex == closingIndex)
                        {
                            int startPos = opening.position;
                            int endPos = consoleOutput.Text.Length - line.Length - 1 + i + 1;
                            string code = consoleOutput.Text.Substring(startPos, endPos - startPos);
                            bracketCodes.Add((code, startPos, endPos));
                        }
                    }
                }
            }
        }

        private void DrawAttempts()
        {
            StringBuilder sb = new();
            sb.Append("\n");

            if (attempts == 4)
                sb.Append("ATTEMPTS REMAINING: ■ ■ ■ ■");
            else if (attempts == 3)
                sb.Append("ATTEMPTS REMAINING: ■ ■ ■ □");
            else if (attempts == 2)
                sb.Append("ATTEMPTS REMAINING: ■ ■ □ □");
            else if (attempts == 1)
                sb.Append("ATTEMPTS REMAINING: ■ □ □ □");

            AppendText(sb.ToString(), attempts > 1 ? terminalGreen : terminalError);
        }

        private void ConsoleOutput_MouseClick(object sender, MouseEventArgs e)
        {
            if (!bootSequenceComplete || !gameInProgress || showingHelp) return;

            int index = consoleOutput.GetCharIndexFromPosition(e.Location);

            // Check bracket codes first (they take priority)
            foreach (var code in bracketCodes)
            {
                if (index >= code.start && index < code.end)
                {
                    PlaySound(terminalBeep);
                    ProcessBracketCode(code.code);
                    return;
                }
            }

            // Then check for words
            foreach (var word in wordRanges)
            {
                if (index >= word.start && index < word.end)
                {
                    PlaySound(terminalBeep);
                    ProcessGuess(word.word);
                    return;
                }
            }
        }

        private void ProcessBracketCode(string code)
        {
            // 50% chance to remove dud, 50% chance to replenish tries
            if (random.Next(2) == 0 && wordList.Count > 1)
            {
                RemoveDud();
            }
            else
            {
                ReplenishTries();
            }
        }

        private void ReplenishTries()
        {
            if (attempts < 4)
            {
                attempts = 4;
                bracketCodes.Clear(); // Remove all bracket codes after use
                DrawMemoryDump();
                AppendText("\n> ALLOWANCE REPLENISHED", terminalHighlight);
            }
        }

        private void RemoveDud()
        {
            var duds = wordList.Where(w => w != password).ToList();
            if (duds.Count > 0)
            {
                string dud = duds[random.Next(duds.Count)];
                wordList.Remove(dud);
                bracketCodes.Clear(); // Remove all bracket codes after use
                DrawMemoryDump();
                AppendText("\n> DUD REMOVED", terminalHighlight);
            }
        }

        private void ConsoleOutput_MouseMove(object sender, MouseEventArgs e)
        {
            if (!bootSequenceComplete || !gameInProgress || showingHelp) return;

            // Reset all selection highlighting
            consoleOutput.SelectionStart = 0;
            consoleOutput.SelectionLength = consoleOutput.TextLength;
            consoleOutput.SelectionBackColor = terminalBackground;
            consoleOutput.SelectionColor = terminalGreen;
            consoleOutput.SelectionLength = 0;

            int index = consoleOutput.GetCharIndexFromPosition(e.Location);

            // Check bracket codes first
            foreach (var code in bracketCodes)
            {
                if (index >= code.start && index < code.end)
                {
                    consoleOutput.SelectionStart = code.start;
                    consoleOutput.SelectionLength = code.end - code.start;
                    consoleOutput.SelectionBackColor = Color.DarkGoldenrod;
                    consoleOutput.SelectionColor = terminalHighlight;
                    return;
                }
            }

            // Then check for words
            foreach (var word in wordRanges)
            {
                if (index >= word.start && index < word.end)
                {
                    consoleOutput.SelectionStart = word.start;
                    consoleOutput.SelectionLength = word.word.Length;
                    consoleOutput.SelectionBackColor = Color.DarkOliveGreen;
                    return;
                }
            }
        }

        private void ConsoleOutput_MouseLeave(object sender, EventArgs e)
        {
            consoleOutput.SelectionLength = 0;
        }

        private void ProcessGuess(string guess)
        {
            string likeness = engine.MakeGuess(guess);
            lastGuess = guess;

            if (likeness == "correct")
            {
                PlaySound(terminalSuccess);
                lastLikeness = -1;
                guessHistory.Add($"{guess} - SUCCESS!");
                ShowSuccessScreen();
            }
            else
            {
                lastLikeness = int.Parse(likeness);
                guessHistory.Add($"{guess} - Likeness: {lastLikeness}/{wordLength}");
                PlaySound(terminalFail);
                attempts--;

                if (attempts <= 0)
                {
                    ShowLockoutScreen();
                }
                else
                {
                    DrawMemoryDump();
                }
            }
        }

        private async void ShowSuccessScreen()
        {
            consoleOutput.Clear();
            AddTextToTypingQueue("> ACCESS GRANTED\n\n", terminalHighlight);
            await WaitForTyping();

            AddTextToTypingQueue("SYSTEM UNLOCKED\n", terminalGreen);
            await WaitForTyping();
            await Task.Delay(500);

            AddTextToTypingQueue("WELCOME TO ROBCO INDUSTRIES (TM) TERMLINK\n", terminalGreen);
            await WaitForTyping();
            await Task.Delay(500);

            gameInProgress = false;

            AddTextToTypingQueue("\nPress ENTER to start a new game or ESC to exit.", terminalDim);
            this.KeyDown += RestartGame_KeyDown;
        }

        private async void ShowLockoutScreen()
        {
            consoleOutput.Clear();
            AddTextToTypingQueue("> ACCESS DENIED\n\n", terminalError);
            await WaitForTyping();

            AddTextToTypingQueue("TERMINAL LOCKED\n", terminalError);
            await WaitForTyping();
            await Task.Delay(500);

            AddTextToTypingQueue($"CORRECT PASSWORD WAS: {password}\n", terminalHighlight);
            await WaitForTyping();
            await Task.Delay(500);

            gameInProgress = false;

            AddTextToTypingQueue("\nPress ENTER to restart or ESC to exit.", terminalDim);
            this.KeyDown += RestartGame_KeyDown;
        }

        private void RestartGame_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                this.KeyDown -= RestartGame_KeyDown;
                ResetGame();
                ShowDifficultySelection();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                this.Close();
            }
        }

        private void ResetGame()
        {
            attempts = 4;
            lastGuess = string.Empty;
            lastLikeness = 0;
            guessHistory.Clear();
            gameInProgress = false;
            bootSequenceComplete = false;
            wordRanges.Clear();
            bracketCodes.Clear();
        }

        private List<string> GenerateHexAddresses()
        {
            List<string> addresses = new();
            int baseAddress = 0xF000 + random.Next(0x1000);

            for (int i = 0; i < 16; i++)
            {
                addresses.Add("0x" + (baseAddress + i * 0x10).ToString("X4"));
            }
            return addresses;
        }

        private void AppendText(string text, Color color)
        {
            consoleOutput.SelectionStart = consoleOutput.TextLength;
            consoleOutput.SelectionLength = 0;
            consoleOutput.SelectionColor = color;
            consoleOutput.AppendText(text);
            consoleOutput.SelectionLength = 0;
        }

        private void ConsoleOutput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.Close();
            }
        }

        private void PlaySound(System.Media.SoundPlayer player)
        {
            try
            {
                player?.Play();
            }
            catch
            {
                // Fall back to system sounds if custom sounds fail
                try
                {
                    if (player == terminalBeep)
                        System.Media.SystemSounds.Asterisk.Play();
                    else if (player == terminalSuccess)
                        System.Media.SystemSounds.Exclamation.Play();
                    else if (player == terminalFail)
                        System.Media.SystemSounds.Hand.Play();
                    else
                        System.Media.SystemSounds.Beep.Play();
                }
                catch
                {
                    // Silently fail if all sound attempts fail
                }
            }
        }
    }

    public class TerminalEngine
    {
        private List<string> masterWordList;
        private List<string> gameWords;
        private string password;
        private int wordLength;
        private Random random = new();
        private HashSet<char> allowedChars;

        // Additional character sets for memory dump
        private readonly string[] specialBracketPairs = new[] { "[]", "{}", "()", "<>" };
        private readonly string specialChars = ".,:;'\"!@#$%^&*-+=_|\\/?";

        public TerminalEngine()
        {
            masterWordList = new();
            gameWords = new();
            allowedChars = new();
            SetupAllowedChars();
        }

        private void SetupAllowedChars()
        {
            // Add alphanumeric characters
            for (char c = 'A'; c <= 'Z'; c++)
                allowedChars.Add(c);

            for (char c = '0'; c <= '9'; c++)
                allowedChars.Add(c);

            // Add special characters
            foreach (char c in specialChars)
                allowedChars.Add(c);

            // Add bracket characters
            foreach (string pair in specialBracketPairs)
            {
                allowedChars.Add(pair[0]);
                allowedChars.Add(pair[1]);
            }
        }

        public void LoadWordList()
        {
            // Real Fallout-style word list
            masterWordList = new List<string> {
                // 6-letter words
                "ACCEPT", "ACCESS", "ALMOST", "ANIMAL", "ATTACK", "BETTER", "BRIDGE", "BURNED", "BUTTON", "CALLED",
                "CHANCE", "CHANGE", "CIPHER", "COLONY", "COMING", "COMMON", "CREATE", "DANGER", "DECODE", "DEFEND",
                "DEMAND", "DETAIL", "DETECT", "DEVICE", "DIRECT", "EASILY", "EFFECT", "ENERGY", "ENGAGE", "ESCAPE",
                "EXPECT", "EXTEND", "FACTOR", "FAILED", "FAMILY", "FASTER", "FATHER", "FILTER", "FORMED", "FRIEND",
                "FUTURE", "GARDEN", "GATHER", "GIVING", "GLANCE", "GROUND", "GROWTH", "HAPPEN", "HAVING", "HIDDEN",
                "HIGHER", "HONEST", "IMPACT", "IMPORT", "INSIDE", "INTEND", "INVEST", "ISLAND", "ITSELF", "JOINED",
                "KILLED", "LAUNCH", "LEADER", "LETTER", "LISTEN", "LITTLE", "LOCKUP", "MATTER", "MEMORY", "MIGHT",
                "MINUTE", "MISSION", "MODIFY", "MODULE", "MOMENT", "MOSTLY", "MOTHER", "MOTION", "MOVING", "MYSELF",
                
                // 7-letter words
                "ABANDON", "ACADEMY", "ACCOUNT", "ACTIONS", "ADDRESS", "ADVANCE", "AGAINST", "ALLOWED", "ALREADY", "ALTERED",
                "AMAZING", "ANCIENT", "ANDROID", "ANIMALS", "ANOTHER", "ANSWERS", "ANYBODY", "APPLIED", "ARRANGE", "ARRIVED",
                "ARSENAL", "ARTICLE", "ASSAULT", "ASSUMED", "ATTEMPT", "ATTRACT", "BENEATH", "BENEFIT", "BESIDES", "BETWEEN",
                "BIOLOGY", "BLESSED", "BOMBING", "BROTHER", "BROUGHT", "BUILDER", "BURNING", "CALLING", "CAPABLE", "CAPITAL",
                "CAPTAIN", "CENTRAL", "CHAIRMAN", "CHANGED", "CHARGED", "CHOICES", "CITIZEN", "CLAIMED", "CLASSES", "CLASSIC",
                "CLEARED", "COMMAND", "COMPLEX", "CONCERN", "CONTACT", "CONTAIN", "CONTENT", "CONTROL", "CONVERT", "CORRECT",
                "COUNCIL", "COUNTER", "COVERED", "CREATED", "CULTURE", "CURRENT", "CUTTING", "DAMAGED", "DEALING", "DECIDED",
                "DELAYED", "DELIVER", "DEMANDS", "DEPOSIT", "DESKTOP", "DESPITE", "DESTROY", "DETAILS", "DEVICES", "DEVOTED",
                
                // 8-letter words
                "ABSOLUTE", "ACADEMY", "ACCEPTED", "ACCIDENT", "ACCURACY", "ACCURATE", "ACHIEVED", "ACQUIRED", "ACTIVITY", "ACTUALLY",
                "ADDITION", "ADJUSTED", "ADVANCED", "ADVISORY", "ADVOCATE", "AFFECTED", "AIRCRAFT", "ALLIANCE", "ALTHOUGH", "ANALYSIS",
                "ANNOUNCE", "ANYTHING", "APPARENT", "ASSEMBLY", "ASSIGNED", "ATHLETIC", "AUDIENCE", "AVIATION", "BACHELOR", "BACTERIA",
                "BASELINE", "BECOMING", "BENJAMIN", "BIRTHDAY", "BOUNDARY", "BREAKING", "BREEDING", "BUILDING", "BULLETIN", "BUSINESS",
                
                // 9-letter words
                "ABANDONED", "ABILITIES", "ACCEPTING", "ACCESSORY", "ACCORDING", "ACHIEVING", "ACQUIRING", "ADDICTION", "ADDITIONS", "ADDRESSED",
                "ADJUSTING", "ADVANTAGE", "ADVENTURE", "ADVERTISE", "AFFECTING", "AGREEMENT", "ALGORITHM", "ALIGNMENT", "ALLOWANCE", "ALTERNATE",
                "AMENDMENT", "AMERICANS", "ANALYZING", "ANNOUNCED", "ANONYMOUS", "ANSWERING", "APARTMENT", "APOLOGIZE", "APPARATUS", "APPEARING"
            };
        }

        public void InitializeGame(int length)
        {
            wordLength = length;
            var filtered = masterWordList.Where(w => w.Length == length).ToList();

            if (filtered.Count < 12)
            {
                // Fallback if not enough words of the requested length
                filtered = masterWordList.Where(w => Math.Abs(w.Length - length) <= 1).ToList();
            }

            // Get 10-15 random words depending on difficulty
            int wordCount = 10 + Math.Min(length - 5, 5);
            gameWords = filtered.OrderBy(x => random.Next()).Take(wordCount).ToList();

            // Ensure all words are exactly the required length (pad if needed)
            for (int i = 0; i < gameWords.Count; i++)
            {
                if (gameWords[i].Length < length)
                {
                    gameWords[i] = gameWords[i].PadRight(length, '.');
                }
                else if (gameWords[i].Length > length)
                {
                    gameWords[i] = gameWords[i].Substring(0, length);
                }
            }

            password = gameWords[random.Next(gameWords.Count)];
        }

        public List<string> GetWordList() => gameWords;

        public string GetPassword() => password;

        public string MakeGuess(string guess)
        {
            if (guess == password) return "correct";

            int likeness = 0;
            for (int i = 0; i < Math.Min(guess.Length, password.Length); i++)
            {
                if (guess[i] == password[i])
                    likeness++;
            }

            return likeness.ToString();
        }

        public Dictionary<string, string> GetFormattedMemoryDump(List<string> hexes, List<string> words)
        {
            var dump = new Dictionary<string, string>();
            int lineLength = 24; // Length of data per hex address

            // Create a sequence of random characters
            List<char> charPool = allowedChars.ToList();

            foreach (var hex in hexes)
            {
                StringBuilder line = new StringBuilder(lineLength);

                // Fill with random chars first
                for (int i = 0; i < lineLength; i++)
                {
                    line.Append(charPool[random.Next(charPool.Count)]);
                }

                // Insert bracket pairs at random positions (30% chance per line)
                if (random.NextDouble() < 0.3)
                {
                    int bracketStart = random.Next(0, lineLength - 3);
                    string bracketPair = specialBracketPairs[random.Next(specialBracketPairs.Length)];
                    line[bracketStart] = bracketPair[0];
                    line[bracketStart + 1] = bracketPair[1];
                }

                // Insert words randomly
                bool wordPlaced = false;
                foreach (string word in words.OrderBy(x => random.Next()))
                {
                    if (wordPlaced) break; // Limit to one word per line for better formatting

                    // Try to place the word at a random position
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        int startPos = random.Next(0, lineLength - word.Length + 1);
                        bool canPlace = true;

                        // Check if this position already has a word
                        // A proper check would involve more complex overlapping detection
                        if (line.ToString().Substring(startPos, word.Length).Contains(word))
                            canPlace = false;

                        if (canPlace)
                        {
                            // Place the word
                            for (int i = 0; i < word.Length; i++)
                            {
                                line[startPos + i] = word[i];
                            }
                            wordPlaced = true;
                            break;
                        }
                    }
                }

                dump[hex] = line.ToString();
            }

            // Ensure the password and all words are placed in the dump
            EnsureWordsPlacement(dump, hexes, words, password);

            return dump;
        }

        private void EnsureWordsPlacement(Dictionary<string, string> dump, List<string> hexes, List<string> words, string password)
        {
            // Make sure password is placed
            bool passwordPlaced = false;
            foreach (var hex in hexes)
            {
                if (dump[hex].Contains(password))
                {
                    passwordPlaced = true;
                    break;
                }
            }

            if (!passwordPlaced)
            {
                // Force place password
                string randomHex = hexes[random.Next(hexes.Count)];
                string data = dump[randomHex];
                int insertPos = random.Next(0, data.Length - password.Length);
                StringBuilder sb = new StringBuilder(data);
                for (int i = 0; i < password.Length; i++)
                {
                    sb[insertPos + i] = password[i];
                }
                dump[randomHex] = sb.ToString();
            }

            // Make sure all words are placed at least once
            foreach (string word in words)
            {
                bool wordPlaced = false;
                foreach (var hex in hexes)
                {
                    if (dump[hex].Contains(word))
                    {
                        wordPlaced = true;
                        break;
                    }
                }

                if (!wordPlaced)
                {
                    // Force place word
                    string randomHex = hexes[random.Next(hexes.Count)];
                    string data = dump[randomHex];
                    int insertPos = random.Next(0, data.Length - word.Length);
                    StringBuilder sb = new StringBuilder(data);
                    for (int i = 0; i < word.Length; i++)
                    {
                        sb[insertPos + i] = word[i];
                    }
                    dump[randomHex] = sb.ToString();
                }
            }
        }
    }
}