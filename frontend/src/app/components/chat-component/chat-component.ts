import { Component, ElementRef, HostListener, effect, input, output, signal, viewChild } from '@angular/core';
import { NgClass } from '@angular/common';
import { ConversationMessage } from '../../interfaces/agent_dtos';

@Component({
  selector: 'app-chat-component',
  standalone: true,
  imports: [NgClass],
  host: { class: 'flex h-full min-h-0 w-full min-w-0 flex-1' },
  templateUrl: './chat-component.html',
})
export class ChatComponent {
  readonly messages = input<ConversationMessage[]>([]);
  readonly conversationTitle = input<string | null>(null);
  readonly loading = input(false);
  readonly sending = input(false);
  readonly progress = input('');
  readonly sendMessage = output<string>();
  readonly toggleSidebar = output<void>();

  readonly draft = signal('');
  private readonly messageList = viewChild<ElementRef<HTMLElement>>('messageList');
  private readonly composerInput = viewChild<ElementRef<HTMLTextAreaElement>>('composerInput');

  constructor() {
    effect(() => {
      this.messages();
      this.loading();
      this.sending();
      this.progress();
      queueMicrotask(() => {
        const list = this.messageList()?.nativeElement;
        if (list) list.scrollTop = list.scrollHeight;
      });
    });
  }

  @HostListener('document:keydown', ['$event'])
  onDocumentKeydown(event: KeyboardEvent): void {
    if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey || event.isComposing) return;
    const target = event.target;
    if (target instanceof HTMLElement && (
      target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON'].includes(target.tagName)
    )) return;
    if (event.key.length !== 1 || this.sending()) return;

    event.preventDefault();
    this.draft.update((draft) => draft + event.key);
    queueMicrotask(() => this.composerInput()?.nativeElement.focus());
  }

  onDraftInput(event: Event): void {
    const input = event.target as HTMLTextAreaElement;
    this.draft.set(input.value);
    input.style.height = 'auto';
    input.style.height = `${Math.min(input.scrollHeight, 180)}px`;
  }

  onComposerKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.submit();
    }
  }

  choosePrompt(prompt: string): void {
    this.draft.set(prompt);
    queueMicrotask(() => {
      const input = this.composerInput()?.nativeElement;
      if (input) {
        input.focus();
        input.style.height = 'auto';
        input.style.height = `${Math.min(input.scrollHeight, 180)}px`;
      }
    });
  }

  submit(): void {
    const message = this.draft().trim();
    if (!message || this.sending()) return;
    this.sendMessage.emit(message);
    this.draft.set('');
    const input = this.composerInput()?.nativeElement;
    if (input) input.style.height = 'auto';
  }

  formatTime(value: string): string {
    return new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' }).format(new Date(value));
  }

  renderMarkdown(markdown: string): string {
    const escapeHtml = (value: string): string => value
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    const inline = (value: string): string => {
      const code: string[] = [];
      let html = escapeHtml(value).replace(/`([^`]+)`/g, (_match, text: string) => {
        const index = code.push(`<code class="rounded bg-white/10 px-1.5 py-0.5 font-mono text-[.9em] text-zinc-200">${text}</code>`) - 1;
        return `\u0000CODE${index}\u0000`;
      });
      html = html
        .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a class="text-blue-300 underline decoration-blue-300/40 underline-offset-2 hover:text-blue-200" href="$2" target="_blank" rel="noopener noreferrer">$1</a>')
        .replace(/\*\*(.+?)\*\*|__(.+?)__/g, '<strong class="font-semibold text-zinc-100">$1$2</strong>')
        .replace(/\*(.+?)\*|_(.+?)_/g, '<em>$1$2</em>');
      return html.replace(/\u0000CODE(\d+)\u0000/g, (_match, index: string) => code[Number(index)]);
    };

    const lines = markdown.replace(/\r\n?/g, '\n').split('\n');
    const output: string[] = [];
    let paragraph: string[] = [];
    let listType: 'ul' | 'ol' | null = null;
    let codeLines: string[] | null = null;
    const flushParagraph = (): void => {
      if (paragraph.length) output.push(`<p class="my-2 leading-7">${paragraph.map(inline).join('<br>')}</p>`);
      paragraph = [];
    };
    const closeList = (): void => {
      if (listType) output.push(`</${listType}>`);
      listType = null;
    };

    for (const line of lines) {
      if (line.trimStart().startsWith('```')) {
        flushParagraph();
        closeList();
        if (codeLines) {
          output.push(`<pre class="my-3 overflow-x-auto rounded-xl bg-[#191919] p-4 text-xs leading-6 text-zinc-200"><code>${codeLines.join('\n')}</code></pre>`);
          codeLines = null;
        } else codeLines = [];
        continue;
      }
      if (codeLines) {
        codeLines.push(escapeHtml(line));
        continue;
      }
      const heading = line.match(/^\s*(#{1,6})\s+(.+)$/);
      const unordered = line.match(/^\s*[-*+]\s+(.+)$/);
      const ordered = line.match(/^\s*\d+[.)]\s+(.+)$/);
      if (!line.trim()) {
        flushParagraph();
        closeList();
      } else if (/^(?:-{3,}|(?:\*\s*){3,}|(?:_\s*){3,})$/.test(line.trim())) {
        flushParagraph();
        closeList();
        output.push('<hr class="my-5 border-white/10">');
      } else if (heading) {
        flushParagraph();
        closeList();
        const level = heading[1].length;
        const styles = level <= 2 ? 'mt-5 mb-2 text-lg font-semibold text-zinc-100' : 'mt-4 mb-1.5 text-base font-semibold text-zinc-100';
        output.push(`<h${level} class="${styles}">${inline(heading[2])}</h${level}>`);
      } else if (unordered || ordered) {
        flushParagraph();
        const nextType = unordered ? 'ul' : 'ol';
        if (listType !== nextType) {
          closeList();
          listType = nextType;
          output.push(`<${listType} class="my-2 space-y-1 pl-6 ${listType === 'ul' ? 'list-disc' : 'list-decimal'}">`);
        }
        output.push(`<li class="pl-1 leading-7">${inline((unordered ?? ordered)![1])}</li>`);
      } else {
        closeList();
        paragraph.push(line);
      }
    }
    flushParagraph();
    closeList();
    if (codeLines) output.push(`<pre class="my-3 overflow-x-auto rounded-xl bg-[#191919] p-4 text-xs leading-6 text-zinc-200"><code>${codeLines.join('\n')}</code></pre>`);
    return output.join('');
  }
}
