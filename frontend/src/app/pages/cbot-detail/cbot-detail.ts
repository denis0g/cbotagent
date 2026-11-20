import { DatePipe } from '@angular/common';
import { Component, ElementRef, HostListener, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CbotDetail as Cbot } from '../../interfaces/cbot_dtos';
import { CbotsService } from '../../services/cbotsService';

interface CbotParameter {
  name: string;
  type: string;
  defaultValue: string | null;
}

@Component({
  imports: [DatePipe, RouterLink],
  host: { class: 'block min-h-full w-full' },
  templateUrl: './cbot-detail.html',
})
export class CbotDetail implements OnInit {
  @ViewChild('sourceEditor') private sourceEditor?: ElementRef<HTMLTextAreaElement>;
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(CbotsService);
  readonly bot = signal<Cbot | null>(null);
  readonly source = signal('');
  readonly parameters = computed(() => this.readParameters(this.source()));
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly compiling = signal(false);
  readonly error = signal('');
  readonly notice = signal('');

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('botId');
    if (!id) { this.error.set('cBot not found.'); this.loading.set(false); return; }
    this.load(id);
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    this.resizeSourceEditor();
  }

  onSourceInput(editor: HTMLTextAreaElement): void {
    this.source.set(editor.value);
    this.resizeSourceEditor();
  }

  saveSource(): void {
    const bot = this.bot();
    if (!bot || this.saving()) return;
    this.saving.set(true);
    this.error.set('');
    this.service.saveSource(bot.id, this.source()).subscribe({
      next: () => {
        this.saving.set(false);
        this.notice.set('Source saved. Compile it to update the .algo file.');
        this.load(bot.id);
      },
      error: () => { this.saving.set(false); this.error.set('Could not save the source code.'); },
    });
  }

  compile(): void {
    const bot = this.bot();
    if (!bot || this.compiling()) return;
    this.compiling.set(true);
    this.error.set('');
    this.notice.set('');
    this.service.saveSource(bot.id, this.source()).subscribe({
      next: () => this.service.compile(bot.id).subscribe({
        next: (result) => {
          this.compiling.set(false);
          if (result.succeeded) this.notice.set('Compilation succeeded. The .algo file is ready to download.');
          else this.error.set(result.output || 'Compilation failed.');
          this.load(bot.id);
        },
        error: () => { this.compiling.set(false); this.error.set('The compile request failed.'); this.load(bot.id); },
      }),
      error: () => { this.compiling.set(false); this.error.set('Could not save the edited source before compiling.'); },
    });
  }

  download(artifact: 'source' | 'algo'): void {
    const bot = this.bot();
    if (!bot) return;
    this.service.download(bot.id, artifact).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = `${bot.name}.${artifact === 'algo' ? 'algo' : 'cs'}`;
        anchor.click();
        URL.revokeObjectURL(url);
      },
      error: () => this.error.set(`Could not download the ${artifact === 'algo' ? '.algo' : 'source'} file.`),
    });
  }

  private load(id: string): void {
    this.service.get(id).subscribe({
      next: (bot) => {
        this.bot.set(bot);
        this.source.set(bot.sourceCode);
        this.loading.set(false);
        requestAnimationFrame(() => this.resizeSourceEditor());
      },
      error: () => { this.error.set('Could not load this cBot.'); this.loading.set(false); },
    });
  }

  private resizeSourceEditor(): void {
    const editor = this.sourceEditor?.nativeElement;
    if (!editor) return;
    editor.style.height = 'auto';
    editor.style.height = `${Math.max(editor.scrollHeight, window.innerHeight * 0.65)}px`;
  }

  private readParameters(source: string): CbotParameter[] {
    const parameters: CbotParameter[] = [];
    const declaration = /\[Parameter\s*\(([\s\S]*?)\)\]\s*(?:(?:public|protected|internal)\s+)?([\w?.<>\[\]]+)\s+([A-Za-z_]\w*)\s*\{\s*get\s*;\s*set\s*;\s*\}/g;
    for (const match of source.matchAll(declaration)) {
      const attributes = match[1];
      const propertyName = match[3];
      const readArgument = (name: string): string | null => {
        const value = attributes.match(new RegExp(`${name}\\s*=\\s*("(?:\\\\.|[^"])*"|'(?:\\\\.|[^'])*'|[^,\\)]+)`))?.[1];
        return value?.trim() ?? null;
      };
      parameters.push({
        name: propertyName,
        type: match[2],
        defaultValue: readArgument('DefaultValue'),
      });
    }
    return parameters;
  }
}
