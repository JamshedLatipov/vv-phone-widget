import re

with open('OrbitalSIP/Views/ExpandedView.axaml', 'r', encoding='utf-8') as f:
    content = f.read()

replacement = """
      <!-- Recent activity -->
      <Border Grid.Row="5" Background="#020617" BorderBrush="#1E293B" BorderThickness="1,1,1,0" Padding="12,7,12,5" MinHeight="60">
        <StackPanel Spacing="6">
          <Grid ColumnDefinitions="*,Auto">
            <TextBlock Text="RECENT ACTIVITY" FontSize="10" FontWeight="Bold" LetterSpacing="1.2" Foreground="#7B92AA"/>
            <Button Grid.Column="1" Name="RefreshCdrBtn" Width="20" Height="20" Background="Transparent" Padding="0" Margin="0" Cursor="Hand" BorderThickness="0">
                <Viewbox Width="12" Height="12" HorizontalAlignment="Center" VerticalAlignment="Center">
                    <Path Fill="#60A5FA" Stretch="Uniform" Data="M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z"/>
                </Viewbox>
            </Button>
          </Grid>

          <ScrollViewer MaxHeight="200" HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
              <ItemsControl Name="CdrItemsControl">
                  <ItemsControl.ItemTemplate>
                      <DataTemplate>
                          <Grid ColumnDefinitions="Auto,*,Auto" Margin="0,4,0,4">
                            <Border Width="24" Height="24" CornerRadius="12" Background="#1E293B">
                              <Viewbox Width="12" Height="12" HorizontalAlignment="Center" VerticalAlignment="Center">
                                <Path Fill="{Binding IconColor}" Stretch="Uniform" Data="{Binding IconData}"/>
                              </Viewbox>
                            </Border>
                            <StackPanel Grid.Column="1" Spacing="0" Margin="8,0,0,0">
                              <TextBlock Text="{Binding DisplayNumber}" FontSize="11" FontWeight="SemiBold" Foreground="#E2E8F0"/>
                              <TextBlock Text="{Binding DisplayTime}" FontSize="9" Foreground="#6E859D"/>
                            </StackPanel>
                            <Button Grid.Column="2"
                                    Width="24" Height="24"
                                    Background="Transparent"
                                    BorderThickness="0"
                                    Padding="5"
                                    Cursor="Hand"
                                    Click="OnCdrCallClicked"
                                    Tag="{Binding DisplayNumber}">
                              <Viewbox Width="14" Height="14" VerticalAlignment="Center">
                                <Path Fill="#60A5FA" Stretch="Uniform"
                                      Data="M6.62,10.79C8.06,13.62 10.38,15.93 13.21,17.38L15.41,15.18C15.68,14.91 16.08,14.82 16.43,14.94C17.55,15.31 18.76,15.51 20,15.51C20.55,15.51 21,15.96 21,16.51V20C21,20.55 20.55,21 20,21C10.61,21 3,13.39 3,4C3,3.45 3.45,3 4,3H7.5C8.05,3 8.5,3.45 8.5,4C8.5,5.25 8.7,6.45 9.07,7.57C9.18,7.92 9.1,8.31 8.82,8.59L6.62,10.79Z" />
                              </Viewbox>
                            </Button>
                          </Grid>
                      </DataTemplate>
                  </ItemsControl.ItemTemplate>
              </ItemsControl>
          </ScrollViewer>
        </StackPanel>
      </Border>
"""

# Try a more robust regex for recent activity replacement
start_pattern = r'<!-- Recent activity -->.*?<Border Grid\.Row="5".*?<!-- Bottom tab bar -->'
content = re.sub(start_pattern, replacement.strip() + '\n\n      <!-- Bottom tab bar -->', content, flags=re.DOTALL)

with open('OrbitalSIP/Views/ExpandedView.axaml', 'w', encoding='utf-8') as f:
    f.write(content)
