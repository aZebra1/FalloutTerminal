using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
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
        private readonly Color terminalBackground = Color.Black;
        private readonly Font terminalFont = new("Consolas", 14, FontStyle.Regular);
        private readonly Color terminalHighlight = Color.Yellow;

        private System.Media.SoundPlayer terminalClick;
        private System.Media.SoundPlayer terminalBeep;

        private bool bootSequenceComplete = false;
        private bool gameInProgress = false;
        private int attempts = 4;
        private Random random = new();

        private TerminalEngine engine;
        private List<string> wordList;
        private string password;

        private Dictionary<string, string> memoryDump;
        private List<(string word, int start, int end)> wordRanges = new();
        private List<string> hexAddresses;

        private int wordLength = 4;
        private string[] difficulties = { "EASY", "NORMAL", "HARD", "VERY HARD" };
        private int currentDifficulty = 0;
        private bool selectingDifficulty = true;

        public TerminalForm()
        {
            InitializeComponents();
            StartBootSequence();
        }

        private void InitializeComponents()
        {
            this.Text = "ROBCO INDUSTRIES (TM) TERMLINK PROTOCOL";
            this.Size = new Size(1000, 600);
            this.BackColor = terminalBackground;
            this.StartPosition = FormStartPosition.CenterScreen;

            consoleOutput = new RichTextBox
            {
                ReadOnly = true,
                BackColor = terminalBackground,
                ForeColor = terminalGreen,
                Font = terminalFont,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Multiline = true,
                TabStop = false
            };
            consoleOutput.MouseClick += ConsoleOutput_MouseClick;
            consoleOutput.MouseMove += ConsoleOutput_MouseMove;
            this.Controls.Add(consoleOutput);

            typingTimer = new System.Windows.Forms.Timer { Interval = 20 };
            typingTimer.Tick += TypingTimer_Tick;

            terminalClick = new System.Media.SoundPlayer();
            terminalBeep = new System.Media.SoundPlayer();

            engine = new TerminalEngine();
            engine.LoadDefaultWordList();
        }

        private void ShowDifficultySelection()
        {
            selectingDifficulty = true;
            consoleOutput.Clear();
            AppendLine("SELECT DIFFICULTY:", terminalGreen);
            for (int i = 0; i < difficulties.Length; i++)
            {
                string line = (i == currentDifficulty ? "> " : "  ") + difficulties[i] + "";
                AppendLine(line, i == currentDifficulty ? terminalHighlight : terminalGreen);
            }
            AppendLine("Use UP / DOWN and ENTER to select.", terminalGreen);
            this.KeyDown += DifficultySelect_KeyDown;
        }

        private void DifficultySelect_KeyDown(object sender, KeyEventArgs e)
        {
            if (!selectingDifficulty) return;

            if (e.KeyCode == Keys.Up)
            {
                currentDifficulty = (currentDifficulty - 1 + difficulties.Length) % difficulties.Length;
                ShowDifficultySelection();
            }
            else if (e.KeyCode == Keys.Down)
            {
                currentDifficulty = (currentDifficulty + 1) % difficulties.Length;
                ShowDifficultySelection();
            }
            else if (e.KeyCode == Keys.Enter)
            {
                this.KeyDown -= DifficultySelect_KeyDown;
                wordLength = 4 + currentDifficulty;
                selectingDifficulty = false;
                StartBootSequence();
            }
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
                if (nextChar != ' ' && nextChar != '\n' && random.Next(10) == 0)
                    PlaySound(terminalClick);
            }
        }

        private async void StartBootSequence()
        {
            consoleOutput.Clear();
            AddTextToTypingQueue("ROBCO INDUSTRIES (TM) TERMLINK PROTOCOL\n", terminalGreen);
            AddTextToTypingQueue("INITIALIZING BOOT SEQUENCE...\n\n", terminalGreen);
            await WaitForTyping();
            await Task.Delay(500);

            AddTextToTypingQueue("SYSTEM INITIALIZING...\nCHECKING MEMORY ARRAYS... OK\n", terminalGreen);
            AddTextToTypingQueue("CHECKING SYSTEM INTEGRITY... OK\n", terminalGreen);
            AddTextToTypingQueue("LOADING ROBCO OS v2.3.0.1...\n", terminalGreen);
            await WaitForTyping();
            await Task.Delay(1000);

            AddTextToTypingQueue("BOOT SEQUENCE COMPLETE\n\n", terminalGreen);
            await WaitForTyping();

            bootSequenceComplete = true;
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

            int columns = 4;
            int rows = hexAddresses.Count / columns;
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
                        line += $"{hex} {data}    ";
                        foreach (string word in wordList)
                        {
                            int pos = data.IndexOf(word);
                            if (pos != -1)
                            {
                                int start = line.Length - data.Length - 4 + pos;
                                wordRanges.Add((word, line.Length - data.Length - 4 + pos, start + word.Length));
                            }
                        }
                    }
                }
                consoleOutput.AppendText(line + "");
            }
            DrawAttempts();
        }

        private void DrawAttempts()
        {
            string squares = new string('■', attempts);
            consoleOutput.AppendText($"\n[{squares.PadRight(4, ' ')}] ATTEMPTS REMAINING\n");
        }

        private void ConsoleOutput_MouseClick(object sender, MouseEventArgs e)
        {
            int index = consoleOutput.GetCharIndexFromPosition(e.Location);
            foreach (var word in wordRanges)
            {
                if (index >= word.start && index < word.end)
                {
                    ProcessGuess(word.word);
                    return;
                }
            }
        }

        private void ConsoleOutput_MouseMove(object sender, MouseEventArgs e)
        {
            int index = consoleOutput.GetCharIndexFromPosition(e.Location);
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
            consoleOutput.SelectionLength = 0;
        }

        private void RemoveDud()
        {
            var duds = wordList.Where(w => w != password).ToList();
            if (duds.Count > 0)
            {
                var dud = duds[random.Next(duds.Count)];
                wordList.Remove(dud);
                StartGame();
                consoleOutput.AppendText($"\n> DUD REMOVED\n");
            }
        }

        private void ProcessGuess(string guess)
        {
            string result = engine.MakeGuess(guess);
            if (result == "correct")
            {
                MessageBox.Show("ACCESS GRANTED", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                gameInProgress = false;
            }
            else
            {
                attempts--;
                if (attempts <= 0)
                {
                    MessageBox.Show("TERMINAL LOCKED", "Failure", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    gameInProgress = false;
                }
                else
                {
                    MessageBox.Show($"ACCESS DENIED Likeness = { result} ", "Try Again", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                DrawMemoryDump();
            }
        }

        private List<string> GenerateHexAddresses()
        {
            List<string> addresses = new();
            for (int i = 0; i < 32; i++)
            {
                addresses.Add("0x" + (0xF000 + i * 0x10).ToString("X4"));
            }
            return addresses;
        }

        private void AppendLine(string text, Color color)
        {
            consoleOutput.SelectionStart = consoleOutput.TextLength;
            consoleOutput.SelectionColor = color;
            consoleOutput.AppendText(text);
        }


        private void ConsoleOutput_KeyDown(object sender, KeyEventArgs e) { }

        private void PlaySound(System.Media.SoundPlayer player)
        {
            try { player?.Play(); } catch { System.Media.SystemSounds.Beep.Play(); }
        }
    }

    public class TerminalEngine
    {
        private List<string> wordList;
        private List<string> gameWords;
        private string password;
        private int wordLength;
        private Random random = new();

        public TerminalEngine()
        {
            wordList = new();
            gameWords = new();
        }

        public void LoadDefaultWordList()
        {
            wordList = new List<string> { "ABLE", "BORN", "COLD", "DATA", "EARN", "FIRE", "GLOW", "HARD", "IRON", "JUMP", "KING", "LOVE", "MOON", "NODE", "OPEN", "PLAN", "QUIT", "RANK", "SEEK", "TRAP", "VAST", "WASH" };
        }


        public void InitializeGame(int length)
        {
            var filtered = wordList.Where(w => w.Length == length).ToList();
            gameWords = filtered.OrderBy(x => random.Next()).Take(10).ToList();
            password = gameWords[random.Next(gameWords.Count)];
        }

        public List<string> GetWordList() => gameWords;
        public string GetPassword() => password;

        public string MakeGuess(string guess)
        {
            if (!gameWords.Contains(guess)) return "invalid";
            if (guess == password) return "correct";

            int likeness = 0;
            for (int i = 0; i < guess.Length; i++)
                if (guess[i] == password[i]) likeness++;

            return likeness.ToString();
        }

        public Dictionary<string, string> GetFormattedMemoryDump(List<string> hexes, List<string> words)
        {
            var dump = new Dictionary<string, string>();
            foreach (var hex in hexes)
            {
                StringBuilder line = new();
                for (int i = 0; i < 12;)
                {
                    if (random.NextDouble() < 0.2 && words.Count > 0)
                    {
                        string word = words[random.Next(words.Count)];
                        line.Append(word);
                        i += word.Length;
                    }
                    else
                    {
                        line.Append((char)random.Next(33, 126));
                        i++;
                    }
                }
                dump[hex] = line.ToString();
            }
            return dump;
        }
    }
}
