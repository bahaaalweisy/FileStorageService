import { formatBytes } from './format-bytes';

describe('formatBytes', () => {
  it('uses plain bytes under 1024', () => {
    expect(formatBytes(512)).toBe('512 B');
  });

  it('labels 1024-based units as KiB/MiB/GiB, not KB/MB/GB', () => {
    expect(formatBytes(200 * 1024 * 1024)).toBe('200.0 MiB');
    expect(formatBytes(1536)).toBe('1.5 KiB');
    expect(formatBytes(1024 * 1024 * 1024 * 2)).toBe('2.0 GiB');
  });

  it('matches the server default UploadPolicy:MaxFileSizeBytes precisely', () => {
    expect(formatBytes(209_715_200)).toBe('200.0 MiB');
  });
});
