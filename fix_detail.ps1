$text = [System.IO.File]::ReadAllText('Diarion.Core/ViewModels/DiaryDetailViewModel.cs', [System.Text.UTF8Encoding]::new($false))
$text = $text.Replace("var todos = await _diaryService.GetTodosForEntryAsync(_currentEntry.Id);", "var todos = await _diaryService.GetTodosForDateAsync(_currentEntry.CreatedAt);")
[System.IO.File]::WriteAllText('Diarion.Core/ViewModels/DiaryDetailViewModel.cs', $text, [System.Text.UTF8Encoding]::new($false))
